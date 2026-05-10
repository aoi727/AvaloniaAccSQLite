using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<BudgetForecastSummary> GetBudgetForecastSummaryAsync(int companyId, DateTime fiscalYearStart)
    {
        var yearStart = fiscalYearStart.Date;
        var yearEnd = yearStart.AddYears(1).AddDays(-1);
        var actualThrough = GetLatestClosedMonthEnd(DateTime.Today, yearStart, yearEnd);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureBudgetForecastSchemaAsync(connection, null);

        var plans = await GetBudgetPlansAsync(connection, companyId, yearStart);
        var openingCashBalance = await GetCashBalanceAsync(companyId, yearStart.AddDays(-1));
        var runningCash = openingCashBalance;
        var rows = new List<BudgetForecastMonthRow>();

        for (var i = 0; i < 12; i++)
        {
            var monthStart = yearStart.AddMonths(i);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            if (monthEnd > yearEnd)
            {
                monthEnd = yearEnd;
            }

            plans.TryGetValue(monthStart, out var plan);
            var actual = await GetMonthlyActualAsync(connection, companyId, monthStart, monthEnd);
            var budgetProfit = (plan?.SalesBudget ?? 0) - (plan?.ExpenseBudget ?? 0);
            var isActualClosed = monthEnd <= actualThrough;
            var landingProfit = isActualClosed ? actual.Profit : budgetProfit;
            var cashMovement = isActualClosed
                ? await GetCashMovementAsync(connection, companyId, monthStart, monthEnd)
                : ResolveForecastCashMovement(plan, budgetProfit);
            runningCash += cashMovement;

            rows.Add(new BudgetForecastMonthRow(
                monthStart,
                monthEnd,
                plan?.SalesBudget ?? 0,
                plan?.ExpenseBudget ?? 0,
                plan?.ExpectedCashIn ?? 0,
                plan?.ExpectedCashOut ?? 0,
                plan?.Note,
                actual.Sales,
                actual.Expenses,
                actual.Profit,
                budgetProfit,
                actual.Profit - budgetProfit,
                isActualClosed,
                landingProfit,
                cashMovement,
                runningCash));
        }

        return new BudgetForecastSummary(
            yearStart,
            yearEnd,
            actualThrough,
            openingCashBalance,
            rows.Sum(x => x.SalesBudget),
            rows.Sum(x => x.ExpenseBudget),
            rows.Sum(x => x.BudgetProfit),
            rows.Where(x => x.IsActualClosed).Sum(x => x.ActualSales),
            rows.Where(x => x.IsActualClosed).Sum(x => x.ActualExpenses),
            rows.Where(x => x.IsActualClosed).Sum(x => x.ActualProfit),
            rows.Sum(x => x.LandingProfit),
            rows.LastOrDefault()?.CashEndingBalance ?? openingCashBalance,
            rows);
    }

    public async Task SaveBudgetPlansAsync(int companyId, int userId, DateTime fiscalYearStart, IReadOnlyList<BudgetPlanInput> plans)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        try
        {
            await EnsureBudgetForecastSchemaAsync(connection, transaction);

            foreach (var plan in plans)
            {
                const string sql = @"
    INSERT INTO monthly_budget_plans (
        company_id,
        fiscal_year_start,
        month_start,
        sales_budget,
        expense_budget,
        expected_cash_in,
        expected_cash_out,
        note,
        updated_at
    )
    VALUES (
        @company_id,
        @fiscal_year_start,
        @month_start,
        @sales_budget,
        @expense_budget,
        @expected_cash_in,
        @expected_cash_out,
        @note,
        CURRENT_TIMESTAMP
    )
    ON CONFLICT(company_id, fiscal_year_start, month_start)
    DO UPDATE SET
        sales_budget = excluded.sales_budget,
        expense_budget = excluded.expense_budget,
        expected_cash_in = excluded.expected_cash_in,
        expected_cash_out = excluded.expected_cash_out,
        note = excluded.note,
        updated_at = CURRENT_TIMESTAMP";

                await using var command = new SqliteCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("company_id", companyId);
                command.Parameters.AddWithValue("fiscal_year_start", fiscalYearStart.Date);
                command.Parameters.AddWithValue("month_start", plan.MonthStart.Date);
                command.Parameters.AddWithValue("sales_budget", plan.SalesBudget);
                command.Parameters.AddWithValue("expense_budget", plan.ExpenseBudget);
                command.Parameters.AddWithValue("expected_cash_in", plan.ExpectedCashIn);
                command.Parameters.AddWithValue("expected_cash_out", plan.ExpectedCashOut);
                command.Parameters.AddWithValue("note", string.IsNullOrWhiteSpace(plan.Note) ? DBNull.Value : plan.Note.Trim());
                await command.ExecuteNonQueryAsync();
            }

            await EnsureOperationLogSchemaAsync(connection, transaction);
            await WriteOperationLogAsync(
                connection,
                transaction,
                companyId,
                userId,
                "budget_forecast_save",
                "monthly_budget_plans",
                fiscalYearStart.ToString("yyyy-MM-dd"),
                $"予算実績・資金繰り見込を保存しました: {fiscalYearStart:yyyy/MM/dd}",
                null);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task EnsureBudgetForecastSchemaAsync(SqliteConnection connection, SqliteTransaction? transaction)
    {
        const string sql = @"
    CREATE TABLE IF NOT EXISTS monthly_budget_plans (
        budget_plan_id     INTEGER PRIMARY KEY AUTOINCREMENT,
        company_id         INTEGER NOT NULL REFERENCES companies(company_id),
        fiscal_year_start  DATE NOT NULL,
        month_start        DATE NOT NULL,
        sales_budget       NUMERIC(15,2) NOT NULL DEFAULT 0,
        expense_budget     NUMERIC(15,2) NOT NULL DEFAULT 0,
        expected_cash_in   NUMERIC(15,2) NOT NULL DEFAULT 0,
        expected_cash_out  NUMERIC(15,2) NOT NULL DEFAULT 0,
        note               TEXT,
        created_at         TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        updated_at         TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(company_id, fiscal_year_start, month_start)
    );
    CREATE INDEX IF NOT EXISTS idx_monthly_budget_plans_company_year
        ON monthly_budget_plans(company_id, fiscal_year_start, month_start);";

        await using var command = new SqliteCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Dictionary<DateTime, BudgetPlanInput>> GetBudgetPlansAsync(
        SqliteConnection connection,
        int companyId,
        DateTime fiscalYearStart)
    {
        const string sql = @"
    SELECT month_start,
           sales_budget,
           expense_budget,
           expected_cash_in,
           expected_cash_out,
           note
    FROM monthly_budget_plans
    WHERE company_id = @company_id
      AND fiscal_year_start = @fiscal_year_start";

        var plans = new Dictionary<DateTime, BudgetPlanInput>();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("fiscal_year_start", fiscalYearStart.Date);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var monthStart = ToDateTime(reader["month_start"]).Date;
            plans[monthStart] = new BudgetPlanInput(
                monthStart,
                reader.GetDecimal(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetString(5));
        }

        return plans;
    }

    private static async Task<(decimal Sales, decimal Expenses, decimal Profit)> GetMonthlyActualAsync(
        SqliteConnection connection,
        int companyId,
        DateTime fromDate,
        DateTime toDate)
    {
        const string sql = @"
    SELECT a.code,
           a.name,
           a.account_type,
           a.balance_side,
           COALESCE(SUM(
               CASE
                   WHEN a.balance_side = 'debit' AND l.side = 'debit' THEN l.amount
                   WHEN a.balance_side = 'debit' AND l.side = 'credit' THEN -l.amount
                   WHEN a.balance_side = 'credit' AND l.side = 'credit' THEN l.amount
                   ELSE -l.amount
               END
           ), 0) AS balance
    FROM accounts a
    LEFT JOIN (
        SELECT l.account_id,
               l.company_id,
               l.side,
               l.amount
        FROM journal_lines l
        JOIN journal_vouchers v ON v.voucher_id = l.voucher_id
        WHERE v.entry_date >= @from_date
          AND v.entry_date <= @to_date
    ) l
      ON l.account_id = a.account_id
     AND l.company_id = a.company_id
    WHERE a.company_id = @company_id
    GROUP BY a.account_id, a.code, a.name, a.account_type, a.balance_side";

        decimal sales = 0;
        decimal expenses = 0;
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("from_date", fromDate.Date);
        command.Parameters.AddWithValue("to_date", toDate.Date);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var accountCode = reader.GetString(0);
            var accountName = reader.GetString(1);
            var accountType = reader.GetString(2);
            var balanceSide = reader.GetString(3);
            var profile = AccountClassificationCatalog.ResolveProfile(accountCode, accountName, accountType, balanceSide);
            if (profile.StatementKind != FinancialStatementKind.IncomeStatement)
            {
                continue;
            }

            var amount = AccountClassificationCatalog.NormalizeBalanceForReports(reader.GetDecimal(4), profile.IsContraAccount);
            if (string.Equals(accountType, "revenue", StringComparison.OrdinalIgnoreCase))
            {
                sales += amount;
            }
            else if (string.Equals(accountType, "expense", StringComparison.OrdinalIgnoreCase))
            {
                expenses += amount;
            }
        }

        return (sales, expenses, sales - expenses);
    }

    private async Task<decimal> GetCashBalanceAsync(int companyId, DateTime asOfDate)
    {
        var summary = await GetBalanceSheetSummaryAsync(companyId, asOfDate);
        return summary.Rows
            .Where(row => string.Equals(row.ClassificationName, "現金・預金", StringComparison.Ordinal))
            .Sum(row => row.ReportBalance);
    }

    private static async Task<decimal> GetCashMovementAsync(
        SqliteConnection connection,
        int companyId,
        DateTime fromDate,
        DateTime toDate)
    {
        const string sql = @"
    SELECT a.code,
           a.name,
           a.account_type,
           a.balance_side,
           COALESCE(SUM(
               CASE
                   WHEN a.balance_side = 'debit' AND l.side = 'debit' THEN l.amount
                   WHEN a.balance_side = 'debit' AND l.side = 'credit' THEN -l.amount
                   WHEN a.balance_side = 'credit' AND l.side = 'credit' THEN l.amount
                   ELSE -l.amount
               END
           ), 0) AS movement
    FROM accounts a
    JOIN journal_lines l ON l.account_id = a.account_id
    JOIN journal_vouchers v ON v.voucher_id = l.voucher_id
    WHERE a.company_id = @company_id
      AND l.company_id = @company_id
      AND v.entry_date >= @from_date
      AND v.entry_date <= @to_date
    GROUP BY a.account_id, a.code, a.name, a.account_type, a.balance_side";

        decimal movement = 0;
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("from_date", fromDate.Date);
        command.Parameters.AddWithValue("to_date", toDate.Date);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var accountCode = reader.GetString(0);
            var accountName = reader.GetString(1);
            var accountType = reader.GetString(2);
            var balanceSide = reader.GetString(3);
            var profile = AccountClassificationCatalog.ResolveProfile(accountCode, accountName, accountType, balanceSide);
            if (string.Equals(profile.ClassificationName, "現金・預金", StringComparison.Ordinal))
            {
                movement += reader.GetDecimal(4);
            }
        }

        return movement;
    }

    private static decimal ResolveForecastCashMovement(BudgetPlanInput? plan, decimal budgetProfit)
    {
        if (plan is null)
        {
            return 0;
        }

        if (plan.ExpectedCashIn != 0 || plan.ExpectedCashOut != 0)
        {
            return plan.ExpectedCashIn - plan.ExpectedCashOut;
        }

        return budgetProfit;
    }

    private static DateTime GetLatestClosedMonthEnd(DateTime today, DateTime fiscalYearStart, DateTime fiscalYearEnd)
    {
        var firstDayOfThisMonth = new DateTime(today.Year, today.Month, 1);
        var latestClosed = firstDayOfThisMonth.AddDays(-1);
        if (latestClosed < fiscalYearStart)
        {
            return fiscalYearStart.AddDays(-1);
        }

        return latestClosed > fiscalYearEnd ? fiscalYearEnd : latestClosed;
    }
}
