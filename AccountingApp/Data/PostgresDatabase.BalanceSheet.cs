using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<BalanceSheetSummary> GetBalanceSheetSummaryAsync(int companyId, DateTime asOfDate)
    {
        var targetDate = asOfDate.Date;
        var fiscalYearStartMonth = await GetFiscalYearStartMonthAsync(companyId);

        const string sql = @"
    WITH balance_history AS (
        SELECT sab.sub_account_id,
               sab.balance,
               date(
                   printf('%04d-%02d-01',
                       CASE
                           WHEN sab.month >= @fiscal_year_start_month THEN sab.fiscal_year
                           ELSE sab.fiscal_year + 1
                       END,
                       sab.month),
                   '+1 month',
                   '-1 day') AS month_end
        FROM sub_account_balances sab
        WHERE sab.company_id = @company_id
    ),
    latest_snapshot AS (
        SELECT sa.sub_account_id,
               sa.account_id,
               sa.balance AS opening_balance,
               (
                   SELECT bh.balance
                   FROM balance_history bh
                   WHERE bh.sub_account_id = sa.sub_account_id
                     AND bh.month_end <= @target_date
                   ORDER BY bh.month_end DESC
                   LIMIT 1
               ) AS snapshot_balance,
               (
                   SELECT bh.month_end
                   FROM balance_history bh
                   WHERE bh.sub_account_id = sa.sub_account_id
                     AND bh.month_end <= @target_date
                   ORDER BY bh.month_end DESC
                   LIMIT 1
               ) AS snapshot_date
        FROM sub_accounts sa
        WHERE sa.company_id = @company_id
          AND sa.is_active = TRUE
    ),
    bridge_changes AS (
        SELECT ls.sub_account_id,
               COALESCE((
                   SELECT SUM(
                       CASE
                           WHEN a.balance_side = 'debit' AND l.side = 'debit' THEN l.amount
                           WHEN a.balance_side = 'debit' AND l.side = 'credit' THEN -l.amount
                           WHEN a.balance_side = 'credit' AND l.side = 'credit' THEN l.amount
                           ELSE -l.amount
                       END
                   )
                   FROM journal_lines l
                   JOIN journal_vouchers v ON v.voucher_id = l.voucher_id
                   LEFT JOIN sub_accounts default_sub
                     ON default_sub.company_id = l.company_id
                    AND default_sub.account_id = l.account_id
                    AND default_sub.code = '0'
                   WHERE l.company_id = @company_id
                     AND COALESCE(NULLIF(l.sub_account_id, 0), default_sub.sub_account_id) = ls.sub_account_id
                     AND v.entry_date <= @target_date
                     AND (ls.snapshot_date IS NULL OR v.entry_date > ls.snapshot_date)
               ), 0) AS bridge_change
        FROM latest_snapshot ls
        JOIN accounts a ON a.account_id = ls.account_id
    ),
    account_balances AS (
        SELECT ls.account_id,
               SUM(COALESCE(ls.snapshot_balance, ls.opening_balance) + COALESCE(bc.bridge_change, 0)) AS balance
        FROM latest_snapshot ls
        LEFT JOIN bridge_changes bc ON bc.sub_account_id = ls.sub_account_id
        GROUP BY ls.account_id
    )
    SELECT a.account_id,
           a.code,
           a.name,
           a.account_type,
           a.balance_side,
           COALESCE(ab.balance, 0) AS balance
    FROM accounts a
    LEFT JOIN account_balances ab ON ab.account_id = a.account_id
    WHERE a.company_id = @company_id
    ORDER BY a.code, a.name";

        var rows = new List<BalanceSheetRow>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("target_date", (object?)(targetDate) ?? DBNull.Value);
        command.Parameters.AddWithValue("fiscal_year_start_month", fiscalYearStartMonth);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var accountCode = reader.GetString(1);
            var accountName = reader.GetString(2);
            var accountType = reader.GetString(3);
            var balanceSide = reader.GetString(4);
            var profile = AccountClassificationCatalog.ResolveProfile(accountCode, accountName, accountType, balanceSide);
            if (profile.StatementKind != FinancialStatementKind.BalanceSheet)
            {
                continue;
            }

            rows.Add(new BalanceSheetRow(
                reader.GetInt32(0),
                accountCode,
                accountName,
                accountType,
                balanceSide,
                profile.ClassificationName,
                profile.ClassificationSortOrder,
                profile.StatementSection,
                reader.GetDecimal(5)));
        }

        var orderedRows = rows
            .OrderBy(x => x.StatementSection == "資産の部" ? 0 : 1)
            .ThenBy(x => x.ClassificationSortOrder)
            .ThenBy(x => AccountClassificationCatalog.GetCodeSortValue(x.AccountCode))
            .ThenBy(x => x.AccountName, StringComparer.Ordinal)
            .ToList();

        var fiscalYearStart = await GetFiscalYearStartDateAsync(companyId, targetDate);
        var currentPeriodNetIncome = await GetCurrentPeriodNetIncomeAsync(companyId, fiscalYearStart, targetDate);

        return new BalanceSheetSummary(orderedRows, currentPeriodNetIncome);
    }

    private async Task<DateTime> GetFiscalYearStartDateAsync(int companyId, DateTime asOfDate)
    {
        const string sql = @"
    SELECT fiscal_year_start
    FROM companies
    WHERE company_id = @company_id";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        var result = await command.ExecuteScalarAsync();
        if (result is null || result == DBNull.Value)
        {
            throw new InvalidOperationException("会社情報が見つかりません。");
        }

        var template = ToDateTime(result).Date;
        var year = asOfDate.Month >= template.Month ? asOfDate.Year : asOfDate.Year - 1;
        var day = Math.Min(template.Day, DateTime.DaysInMonth(year, template.Month));
        return new DateTime(year, template.Month, day);
    }

    private async Task<decimal> GetCurrentPeriodNetIncomeAsync(int companyId, DateTime fromDate, DateTime toDate)
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
    GROUP BY a.account_id, a.code, a.name, a.account_type, a.balance_side
    ORDER BY a.code, a.name";

        decimal netIncome = 0;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("from_date", (object?)(fromDate.Date) ?? DBNull.Value);
        command.Parameters.AddWithValue("to_date", (object?)(toDate.Date) ?? DBNull.Value);

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

            var reportBalance = AccountClassificationCatalog.NormalizeBalanceForReports(reader.GetDecimal(4), profile.IsContraAccount);
            if (string.Equals(accountType, "revenue", StringComparison.OrdinalIgnoreCase))
            {
                netIncome += reportBalance;
            }
            else if (string.Equals(accountType, "expense", StringComparison.OrdinalIgnoreCase))
            {
                netIncome -= reportBalance;
            }
        }

        return netIncome;
    }

    private static DateTime ToDateTime(object value)
    {
        return value switch
        {
            DateTime dateTime => dateTime,
            DateOnly dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue),
            _ => Convert.ToDateTime(value)
        };
    }
}
