using Microsoft.Data.Sqlite;
using ReligiousReportApp.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ReligiousReportApp.Data;

public sealed class AccountingDatabase
{
    private readonly string _connectionString;

    public AccountingDatabase(string databasePath)
    {
        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeReligiousReportSchemaAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        var company = await GetCompanyAsync(connection);
        await SeedDefaultCategoriesAsync(connection, company.CompanyId);
        await SeedAccountRolesAsync(connection, company.CompanyId);
    }

    public async Task<CompanyInfo> GetCompanyAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await GetCompanyAsync(connection);
    }

    public async Task<IReadOnlyList<ReligiousReportCategory>> GetCategoriesAsync(int companyId, bool includeInactive = false)
    {
        const string sql = @"
    SELECT category_id, company_id, code, name, kind, display_order, is_active
    FROM religious_report_categories
    WHERE company_id = @company_id
      AND (@include_inactive = 1 OR is_active = TRUE)
    ORDER BY kind DESC, display_order, code";

        var rows = new List<ReligiousReportCategory>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("include_inactive", includeInactive ? 1 : 0);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new ReligiousReportCategory(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetBoolean(6)));
        }

        return rows;
    }

    public async Task SaveCategoryAsync(ReligiousReportCategory category)
    {
        const string sql = @"
    INSERT INTO religious_report_categories (
        category_id, company_id, code, name, kind, display_order, is_active, updated_at
    )
    VALUES (
        @category_id, @company_id, @code, @name, @kind, @display_order, @is_active, CURRENT_TIMESTAMP
    )
    ON CONFLICT(category_id)
    DO UPDATE SET
        code = excluded.code,
        name = excluded.name,
        kind = excluded.kind,
        display_order = excluded.display_order,
        is_active = excluded.is_active,
        updated_at = CURRENT_TIMESTAMP";

        ValidateCategory(category);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("category_id", (object?)category.CategoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("company_id", category.CompanyId);
        command.Parameters.AddWithValue("code", category.Code.Trim());
        command.Parameters.AddWithValue("name", category.Name.Trim());
        command.Parameters.AddWithValue("kind", category.Kind.Trim());
        command.Parameters.AddWithValue("display_order", category.DisplayOrder);
        command.Parameters.AddWithValue("is_active", category.IsActive);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<AccountRoleRow>> GetAccountRolesAsync(int companyId)
    {
        const string sql = @"
    SELECT a.account_id,
           a.code,
           a.name,
           a.account_type,
           COALESCE(r.role, 'excluded'),
           r.default_category_id,
           c.code,
           c.name
    FROM accounts a
    LEFT JOIN religious_report_account_roles r
      ON r.company_id = a.company_id
     AND r.account_id = a.account_id
    LEFT JOIN religious_report_categories c
      ON c.category_id = r.default_category_id
    WHERE a.company_id = @company_id
      AND a.is_active = TRUE
    ORDER BY a.account_type, a.code, a.name";

        var rows = new List<AccountRoleRow>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await SeedAccountRolesAsync(connection, companyId);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new AccountRoleRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return rows;
    }

    public async Task SaveAccountRolesAsync(int companyId, IReadOnlyList<AccountRoleRow> rows)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        try
        {
            foreach (var row in rows)
            {
                const string sql = @"
    INSERT INTO religious_report_account_roles (
        company_id, account_id, role, default_category_id, account_code_snapshot, account_name_snapshot, updated_at
    )
    VALUES (@company_id, @account_id, @role, @default_category_id, @account_code_snapshot, @account_name_snapshot, CURRENT_TIMESTAMP)
    ON CONFLICT(company_id, account_id)
    DO UPDATE SET
        role = excluded.role,
        default_category_id = excluded.default_category_id,
        account_code_snapshot = excluded.account_code_snapshot,
        account_name_snapshot = excluded.account_name_snapshot,
        updated_at = CURRENT_TIMESTAMP";
                await using var command = new SqliteCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("company_id", companyId);
                command.Parameters.AddWithValue("account_id", row.AccountId);
                command.Parameters.AddWithValue("role", row.Role);
                command.Parameters.AddWithValue("default_category_id", (object?)row.DefaultCategoryId ?? DBNull.Value);
                command.Parameters.AddWithValue("account_code_snapshot", row.AccountCode);
                command.Parameters.AddWithValue("account_name_snapshot", row.AccountName);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<long, decimal>> GetBudgetsAsync(int companyId, DateTime fiscalYearStart)
    {
        const string sql = @"
    SELECT category_id, budget_amount
    FROM religious_report_budgets
    WHERE company_id = @company_id
      AND fiscal_year_start = @fiscal_year_start";

        var budgets = new Dictionary<long, decimal>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("fiscal_year_start", fiscalYearStart.Date);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            budgets[reader.GetInt64(0)] = reader.GetDecimal(1);
        }

        return budgets;
    }

    public async Task SaveBudgetsAsync(int companyId, DateTime fiscalYearStart, IReadOnlyDictionary<long, decimal> budgets)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        try
        {
            foreach (var (categoryId, amount) in budgets)
            {
                const string sql = @"
    INSERT INTO religious_report_budgets (company_id, fiscal_year_start, category_id, budget_amount, updated_at)
    VALUES (@company_id, @fiscal_year_start, @category_id, @budget_amount, CURRENT_TIMESTAMP)
    ON CONFLICT(company_id, fiscal_year_start, category_id)
    DO UPDATE SET budget_amount = excluded.budget_amount, updated_at = CURRENT_TIMESTAMP";
                await using var command = new SqliteCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("company_id", companyId);
                command.Parameters.AddWithValue("fiscal_year_start", fiscalYearStart.Date);
                command.Parameters.AddWithValue("category_id", categoryId);
                command.Parameters.AddWithValue("budget_amount", amount);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<decimal> GetOpeningCarryoverAsync(int companyId, DateTime fiscalYearStart)
    {
        const string sql = @"
    SELECT opening_carryover
    FROM religious_report_carryovers
    WHERE company_id = @company_id
      AND fiscal_year_start = @fiscal_year_start";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("fiscal_year_start", fiscalYearStart.Date);
        var result = await command.ExecuteScalarAsync();
        return result is null || result == DBNull.Value ? 0 : Convert.ToDecimal(result, CultureInfo.InvariantCulture);
    }

    public async Task SaveOpeningCarryoverAsync(int companyId, DateTime fiscalYearStart, decimal openingCarryover)
    {
        const string sql = @"
    INSERT INTO religious_report_carryovers (company_id, fiscal_year_start, opening_carryover, updated_at)
    VALUES (@company_id, @fiscal_year_start, @opening_carryover, CURRENT_TIMESTAMP)
    ON CONFLICT(company_id, fiscal_year_start)
    DO UPDATE SET
        opening_carryover = excluded.opening_carryover,
        updated_at = CURRENT_TIMESTAMP";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("fiscal_year_start", fiscalYearStart.Date);
        command.Parameters.AddWithValue("opening_carryover", openingCarryover);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<CashFlowReviewRow>> GetCashFlowReviewRowsAsync(int companyId, DateTime periodStart, DateTime periodEnd)
    {
        const string sql = @"
    WITH counter_count AS (
        SELECT cash.line_id AS cash_line_id,
               COUNT(*) AS counter_count
        FROM journal_lines cash
        JOIN journal_lines l2
          ON l2.company_id = cash.company_id
         AND l2.voucher_id = cash.voucher_id
         AND l2.line_id <> cash.line_id
        GROUP BY cash.line_id
    )
    SELECT cash.line_id,
           counter.line_id,
           cash.voucher_id,
           COALESCE(v.entry_number, ''),
           v.entry_date,
           COALESCE(counter.description, cash.description, ''),
           cash.side,
           counter.amount,
           ca.code || ' ' || ca.name,
           counter_account.code || ' ' || counter_account.name,
           COALESCE(counter_role.role, 'manual'),
           counter_role.default_category_id,
           override.treatment,
           override.category_id,
           override.note,
           override.source_hash,
           COALESCE(counter_count.counter_count, 0)
    FROM journal_lines cash
    JOIN journal_vouchers v ON v.voucher_id = cash.voucher_id
    JOIN accounts ca ON ca.account_id = cash.account_id
    JOIN journal_lines counter
      ON counter.company_id = cash.company_id
     AND counter.voucher_id = cash.voucher_id
     AND counter.line_id <> cash.line_id
    JOIN accounts counter_account ON counter_account.account_id = counter.account_id
    JOIN religious_report_account_roles cash_role
      ON cash_role.company_id = cash.company_id
     AND cash_role.account_id = cash.account_id
     AND cash_role.role = 'cash'
    LEFT JOIN counter_count ON counter_count.cash_line_id = cash.line_id
    LEFT JOIN religious_report_account_roles counter_role
      ON counter_role.company_id = cash.company_id
     AND counter_role.account_id = counter.account_id
    LEFT JOIN religious_report_cash_flow_split_overrides override
      ON override.company_id = cash.company_id
     AND override.cash_line_id = cash.line_id
     AND override.counter_line_id = counter.line_id
    WHERE cash.company_id = @company_id
      AND v.entry_date >= @period_start
      AND v.entry_date <= @period_end
    ORDER BY v.entry_date, cash.voucher_id, cash.line_id, counter.line_no, counter.line_id";

        var rows = new List<CashFlowReviewRow>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await SeedAccountRolesAsync(connection, companyId);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("period_start", periodStart.Date);
        command.Parameters.AddWithValue("period_end", periodEnd.Date);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var side = reader.GetString(6);
            var direction = side == "debit" ? "income" : "expense";
            var counterRole = reader.GetString(10);
            long? suggestedCategoryId = reader.IsDBNull(11) ? null : reader.GetInt64(11);
            var suggestedTreatment = InferTreatment(direction, counterRole, suggestedCategoryId);
            var overrideTreatment = reader.IsDBNull(12) ? null : reader.GetString(12);
            long? overrideCategoryId = reader.IsDBNull(13) ? null : reader.GetInt64(13);
            var sourceSnapshot = BuildSourceSnapshot(
                ToDateTime(reader[4]).Date,
                reader.GetString(3),
                reader.GetString(5),
                direction,
                reader.GetDecimal(7),
                reader.GetString(8),
                reader.GetString(9));
            var sourceHash = BuildSourceHash(sourceSnapshot);
            var savedHash = reader.IsDBNull(15) ? null : reader.GetString(15);
            var isChanged = savedHash is not null && !string.Equals(savedHash, sourceHash, StringComparison.Ordinal);

            rows.Add(new CashFlowReviewRow(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetString(3),
                ToDateTime(reader[4]).Date,
                reader.GetString(5),
                direction,
                reader.GetDecimal(7),
                reader.GetString(8),
                reader.GetString(9),
                counterRole,
                reader.GetInt32(16) > 1,
                isChanged,
                sourceHash,
                sourceSnapshot,
                suggestedTreatment,
                suggestedCategoryId,
                overrideTreatment ?? suggestedTreatment,
                overrideTreatment is null ? suggestedCategoryId : overrideCategoryId,
                reader.IsDBNull(14) ? null : reader.GetString(14)));
        }

        return rows;
    }

    public async Task SaveCashFlowOverridesAsync(int companyId, IReadOnlyList<CashFlowOverrideInput> rows)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        try
        {
            foreach (var row in rows)
            {
                const string sql = @"
    INSERT INTO religious_report_cash_flow_split_overrides (
        company_id, cash_line_id, counter_line_id, source_hash, source_snapshot, treatment, category_id, note, updated_at
    )
    VALUES (@company_id, @cash_line_id, @counter_line_id, @source_hash, @source_snapshot, @treatment, @category_id, @note, CURRENT_TIMESTAMP)
    ON CONFLICT(company_id, cash_line_id, counter_line_id)
    DO UPDATE SET
        source_hash = excluded.source_hash,
        source_snapshot = excluded.source_snapshot,
        treatment = excluded.treatment,
        category_id = excluded.category_id,
        note = excluded.note,
        updated_at = CURRENT_TIMESTAMP";
                await using var command = new SqliteCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("company_id", companyId);
                command.Parameters.AddWithValue("cash_line_id", row.CashLineId);
                command.Parameters.AddWithValue("counter_line_id", row.CounterLineId);
                command.Parameters.AddWithValue("source_hash", row.SourceHash);
                command.Parameters.AddWithValue("source_snapshot", row.SourceSnapshot);
                command.Parameters.AddWithValue("treatment", row.Treatment);
                command.Parameters.AddWithValue("category_id", (object?)row.CategoryId ?? DBNull.Value);
                command.Parameters.AddWithValue("note", string.IsNullOrWhiteSpace(row.Note) ? DBNull.Value : row.Note.Trim());
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task SavePeriodReviewStatusAsync(int companyId, DateTime periodStart, DateTime periodEnd, string status, string? note = null)
    {
        const string sql = @"
    INSERT INTO religious_report_period_reviews (company_id, period_start, period_end, status, note, reviewed_at, finalized_at, updated_at)
    VALUES (
        @company_id,
        @period_start,
        @period_end,
        @status,
        @note,
        CURRENT_TIMESTAMP,
        CASE WHEN @status = 'finalized' THEN CURRENT_TIMESTAMP ELSE NULL END,
        CURRENT_TIMESTAMP
    )
    ON CONFLICT(company_id, period_start, period_end)
    DO UPDATE SET
        status = excluded.status,
        note = excluded.note,
        reviewed_at = CASE WHEN excluded.status = 'reviewed' THEN CURRENT_TIMESTAMP ELSE reviewed_at END,
        finalized_at = CASE WHEN excluded.status = 'finalized' THEN CURRENT_TIMESTAMP ELSE finalized_at END,
        updated_at = CURRENT_TIMESTAMP";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("period_start", periodStart.Date);
        command.Parameters.AddWithValue("period_end", periodEnd.Date);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("note", string.IsNullOrWhiteSpace(note) ? DBNull.Value : note.Trim());
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> UnfinalizePeriodReviewAsync(int companyId, DateTime periodStart, DateTime periodEnd)
    {
        const string sql = @"
    UPDATE religious_report_period_reviews
    SET status = 'reviewed',
        reviewed_at = COALESCE(reviewed_at, CURRENT_TIMESTAMP),
        finalized_at = NULL,
        updated_at = CURRENT_TIMESTAMP
    WHERE company_id = @company_id
      AND period_start = @period_start
      AND period_end = @period_end
      AND status = 'finalized'";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("period_start", periodStart.Date);
        command.Parameters.AddWithValue("period_end", periodEnd.Date);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<string> GetReportNoteAsync(int companyId, DateTime periodStart, DateTime periodEnd)
    {
        const string sql = @"
    SELECT note
    FROM religious_report_notes
    WHERE company_id = @company_id
      AND period_start = @period_start
      AND period_end = @period_end";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("period_start", periodStart.Date);
        command.Parameters.AddWithValue("period_end", periodEnd.Date);
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.CurrentCulture) ?? "";
    }

    public async Task SaveReportNoteAsync(int companyId, DateTime periodStart, DateTime periodEnd, string? note)
    {
        const string sql = @"
    INSERT INTO religious_report_notes (company_id, period_start, period_end, note, updated_at)
    VALUES (@company_id, @period_start, @period_end, @note, CURRENT_TIMESTAMP)
    ON CONFLICT(company_id, period_start, period_end)
    DO UPDATE SET
        note = excluded.note,
        updated_at = CURRENT_TIMESTAMP";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("period_start", periodStart.Date);
        command.Parameters.AddWithValue("period_end", periodEnd.Date);
        command.Parameters.AddWithValue("note", string.IsNullOrWhiteSpace(note) ? DBNull.Value : note.Trim());
        await command.ExecuteNonQueryAsync();
    }

    public async Task<PeriodReviewStatus?> GetPeriodReviewStatusAsync(int companyId, DateTime periodStart, DateTime periodEnd)
    {
        const string sql = @"
    SELECT period_start, period_end, status, note
    FROM religious_report_period_reviews
    WHERE company_id = @company_id
      AND period_start = @period_start
      AND period_end = @period_end";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("period_start", periodStart.Date);
        command.Parameters.AddWithValue("period_end", periodEnd.Date);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PeriodReviewStatus(
            ToDateTime(reader[0]).Date,
            ToDateTime(reader[1]).Date,
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    public async Task<ReligiousReportSummary> GetReportSummaryAsync(int companyId, DateTime periodStart, DateTime periodEnd)
    {
        var company = await GetCompanyAsync();
        var fiscalStart = GetFiscalYearStartFor(company, periodStart);
        var fiscalEnd = fiscalStart.AddYears(1).AddDays(-1);
        var budgetFactor = GetBudgetFactor(periodStart.Date, periodEnd.Date, fiscalStart, fiscalEnd);
        var categories = await GetCategoriesAsync(companyId);
        var annualBudgets = await GetBudgetsAsync(companyId, fiscalStart);
        var cashRows = await GetCashFlowReviewRowsAsync(companyId, periodStart.Date, periodEnd.Date);

        var actuals = GetActualsByCategory(cashRows);

        var unresolvedCount = cashRows.Count(x => x.IsChanged || x.EffectiveTreatment == "manual" ||
            (x.EffectiveTreatment == "include" && !x.EffectiveCategoryId.HasValue));

        var rows = categories
            .Select(category =>
            {
                var annualBudget = category.CategoryId.HasValue && annualBudgets.TryGetValue(category.CategoryId.Value, out var budgetAmount)
                    ? budgetAmount
                    : 0;
                var budget = Math.Round(annualBudget * budgetFactor, 0, MidpointRounding.AwayFromZero);
                var actual = category.CategoryId.HasValue && actuals.TryGetValue(category.CategoryId.Value, out var actualAmount)
                    ? actualAmount
                    : 0;
                return new ReligiousReportRow(category.Kind, category.Code, category.Name, budget, actual, budget - actual);
            })
            .ToList();

        var incomeRows = rows.Where(x => x.Kind == "income").ToList();
        var expenseRows = rows.Where(x => x.Kind == "expense").ToList();
        var incomeBudget = incomeRows.Sum(x => x.BudgetAmount);
        var incomeActual = incomeRows.Sum(x => x.ActualAmount);
        var expenseBudget = expenseRows.Sum(x => x.BudgetAmount);
        var expenseActual = expenseRows.Sum(x => x.ActualAmount);
        var fiscalOpeningCarryover = await GetOpeningCarryoverAsync(companyId, fiscalStart);
        var previousNetActual = periodStart.Date > fiscalStart
            ? await GetNetActualAsync(companyId, fiscalStart, periodStart.Date.AddDays(-1), categories)
            : 0;
        var periodOpeningCarryover = fiscalOpeningCarryover + previousNetActual;
        var closingCarryover = periodOpeningCarryover + incomeActual - expenseActual;

        return new ReligiousReportSummary(
            periodStart.Date,
            periodEnd.Date,
            fiscalStart,
            fiscalEnd,
            budgetFactor,
            unresolvedCount,
            incomeBudget,
            incomeActual,
            expenseBudget,
            expenseActual,
            incomeBudget - expenseBudget,
            incomeActual - expenseActual,
            fiscalOpeningCarryover,
            periodOpeningCarryover,
            closingCarryover,
            rows);
    }

    public DateTime GetFiscalYearStartFor(CompanyInfo company, DateTime targetDate)
    {
        var template = company.FiscalYearStart.Date;
        var year = targetDate.Month > template.Month ||
                   (targetDate.Month == template.Month && targetDate.Day >= template.Day)
            ? targetDate.Year
            : targetDate.Year - 1;
        var day = Math.Min(template.Day, DateTime.DaysInMonth(year, template.Month));
        return new DateTime(year, template.Month, day);
    }

    private static async Task<CompanyInfo> GetCompanyAsync(SqliteConnection connection)
    {
        const string sql = @"
    SELECT company_id, name, fiscal_year_start, closing_day
    FROM companies
    ORDER BY company_id
    LIMIT 1";

        await using var command = new SqliteCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("AccountingApp の会社情報が見つかりません。");
        }

        return new CompanyInfo(
            reader.GetInt32(0),
            reader.GetString(1),
            ToDateTime(reader[2]).Date,
            reader.GetInt32(3));
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection)
    {
        const string sql = @"
    CREATE TABLE IF NOT EXISTS religious_report_categories (
        category_id    INTEGER PRIMARY KEY AUTOINCREMENT,
        company_id     INTEGER NOT NULL REFERENCES companies(company_id),
        code           VARCHAR(30) NOT NULL,
        name           VARCHAR(100) NOT NULL,
        kind           VARCHAR(20) NOT NULL CHECK (kind IN ('income','expense')),
        display_order  INTEGER NOT NULL DEFAULT 0,
        is_active      BOOLEAN NOT NULL DEFAULT TRUE,
        created_at     TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        updated_at     TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(company_id, code)
    );

    CREATE TABLE IF NOT EXISTS religious_report_account_mappings (
        mapping_id     INTEGER PRIMARY KEY AUTOINCREMENT,
        company_id     INTEGER NOT NULL REFERENCES companies(company_id),
        account_id     INTEGER NOT NULL REFERENCES accounts(account_id),
        category_id    INTEGER NOT NULL REFERENCES religious_report_categories(category_id),
        created_at     TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        updated_at     TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(company_id, account_id)
    );

    CREATE TABLE IF NOT EXISTS religious_report_account_roles (
        role_id               INTEGER PRIMARY KEY AUTOINCREMENT,
        company_id            INTEGER NOT NULL REFERENCES companies(company_id),
        account_id            INTEGER NOT NULL REFERENCES accounts(account_id),
        role                  VARCHAR(30) NOT NULL CHECK (role IN ('cash','income','expense','borrowing','deposit','payable','receivable','excluded','manual')),
        default_category_id   INTEGER REFERENCES religious_report_categories(category_id),
        account_code_snapshot VARCHAR(30) NOT NULL,
        account_name_snapshot VARCHAR(100) NOT NULL,
        created_at            TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        updated_at            TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(company_id, account_id)
    );

    CREATE TABLE IF NOT EXISTS religious_report_cash_flow_overrides (
        override_id  INTEGER PRIMARY KEY AUTOINCREMENT,
        company_id   INTEGER NOT NULL REFERENCES companies(company_id),
        cash_line_id INTEGER NOT NULL REFERENCES journal_lines(line_id),
        treatment    VARCHAR(20) NOT NULL CHECK (treatment IN ('include','exclude','manual')),
        category_id  INTEGER REFERENCES religious_report_categories(category_id),
        note         TEXT,
        created_at   TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        updated_at   TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(company_id, cash_line_id)
    );

    CREATE TABLE IF NOT EXISTS religious_report_cash_flow_split_overrides (
        override_id     INTEGER PRIMARY KEY AUTOINCREMENT,
        company_id      INTEGER NOT NULL REFERENCES companies(company_id),
        cash_line_id    INTEGER NOT NULL REFERENCES journal_lines(line_id),
        counter_line_id INTEGER NOT NULL REFERENCES journal_lines(line_id),
        treatment       VARCHAR(20) NOT NULL CHECK (treatment IN ('include','exclude','manual')),
        category_id     INTEGER REFERENCES religious_report_categories(category_id),
        source_hash     TEXT,
        source_snapshot TEXT,
        note            TEXT,
        created_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        updated_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(company_id, cash_line_id, counter_line_id)
    );

    CREATE TABLE IF NOT EXISTS religious_report_period_reviews (
        review_id    INTEGER PRIMARY KEY AUTOINCREMENT,
        company_id   INTEGER NOT NULL REFERENCES companies(company_id),
        period_start DATE NOT NULL,
        period_end   DATE NOT NULL,
        status       VARCHAR(20) NOT NULL CHECK (status IN ('draft','reviewed','finalized')),
        note         TEXT,
        reviewed_at  TIMESTAMP,
        finalized_at TIMESTAMP,
        created_at   TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        updated_at   TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(company_id, period_start, period_end)
    );

    CREATE TABLE IF NOT EXISTS religious_report_carryovers (
        carryover_id      INTEGER PRIMARY KEY AUTOINCREMENT,
        company_id        INTEGER NOT NULL REFERENCES companies(company_id),
        fiscal_year_start DATE NOT NULL,
        opening_carryover NUMERIC(15,2) NOT NULL DEFAULT 0,
        note              TEXT,
        created_at        TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        updated_at        TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(company_id, fiscal_year_start)
    );

    CREATE TABLE IF NOT EXISTS religious_report_notes (
        note_id      INTEGER PRIMARY KEY AUTOINCREMENT,
        company_id   INTEGER NOT NULL REFERENCES companies(company_id),
        period_start DATE NOT NULL,
        period_end   DATE NOT NULL,
        note         TEXT,
        created_at   TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        updated_at   TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(company_id, period_start, period_end)
    );

    CREATE TABLE IF NOT EXISTS religious_report_budgets (
        budget_id         INTEGER PRIMARY KEY AUTOINCREMENT,
        company_id        INTEGER NOT NULL REFERENCES companies(company_id),
        fiscal_year_start DATE NOT NULL,
        category_id       INTEGER NOT NULL REFERENCES religious_report_categories(category_id),
        budget_amount     NUMERIC(15,2) NOT NULL DEFAULT 0,
        created_at        TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        updated_at        TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(company_id, fiscal_year_start, category_id)
    );

    CREATE INDEX IF NOT EXISTS idx_religious_report_categories_company
        ON religious_report_categories(company_id, kind, display_order);
    CREATE INDEX IF NOT EXISTS idx_religious_report_roles_company
        ON religious_report_account_roles(company_id, account_id, role);
    CREATE INDEX IF NOT EXISTS idx_religious_report_overrides_company
        ON religious_report_cash_flow_overrides(company_id, cash_line_id);
    CREATE INDEX IF NOT EXISTS idx_religious_report_split_overrides_company
        ON religious_report_cash_flow_split_overrides(company_id, cash_line_id, counter_line_id);
    CREATE INDEX IF NOT EXISTS idx_religious_report_reviews_company_period
        ON religious_report_period_reviews(company_id, period_start, period_end);
    CREATE INDEX IF NOT EXISTS idx_religious_report_carryovers_company_year
        ON religious_report_carryovers(company_id, fiscal_year_start);
    CREATE INDEX IF NOT EXISTS idx_religious_report_notes_company_period
        ON religious_report_notes(company_id, period_start, period_end);
    CREATE INDEX IF NOT EXISTS idx_religious_report_budgets_company_year
        ON religious_report_budgets(company_id, fiscal_year_start);";

        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
        await EnsureColumnAsync(connection, "religious_report_cash_flow_split_overrides", "source_hash", "TEXT");
        await EnsureColumnAsync(connection, "religious_report_cash_flow_split_overrides", "source_snapshot", "TEXT");
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        await using (var pragma = new SqliteCommand($"PRAGMA table_info({tableName})", connection))
        await using (var reader = await pragma.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        await using var alter = new SqliteCommand($"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}", connection);
        await alter.ExecuteNonQueryAsync();
    }

    private static async Task SeedDefaultCategoriesAsync(SqliteConnection connection, int companyId)
    {
        var defaults = new[]
        {
            ("I010", "お布施・献金収入", "income", 10),
            ("I020", "会費・維持費収入", "income", 20),
            ("I030", "寄付金収入", "income", 30),
            ("I040", "墓地・納骨堂収入", "income", 40),
            ("I050", "行事収入", "income", 50),
            ("I900", "借入金収入", "income", 900),
            ("I990", "その他収入", "income", 990),
            ("E010", "宗教活動費", "expense", 10),
            ("E020", "人件費", "expense", 20),
            ("E030", "寺務・事務費", "expense", 30),
            ("E040", "維持管理費", "expense", 40),
            ("E050", "行事費", "expense", 50),
            ("E990", "その他支出", "expense", 990)
        };

        foreach (var item in defaults)
        {
            const string insertSql = @"
    INSERT INTO religious_report_categories (company_id, code, name, kind, display_order)
    VALUES (@company_id, @code, @name, @kind, @display_order)
    ON CONFLICT(company_id, code) DO NOTHING";
            await using var command = new SqliteCommand(insertSql, connection);
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("code", item.Item1);
            command.Parameters.AddWithValue("name", item.Item2);
            command.Parameters.AddWithValue("kind", item.Item3);
            command.Parameters.AddWithValue("display_order", item.Item4);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedAccountRolesAsync(SqliteConnection connection, int companyId)
    {
        const string sql = @"
    SELECT a.account_id, a.code, a.name, a.account_type
    FROM accounts a
    LEFT JOIN religious_report_account_roles r
      ON r.company_id = a.company_id
     AND r.account_id = a.account_id
    WHERE a.company_id = @company_id
      AND a.is_active = TRUE
      AND r.role_id IS NULL";

        var categoryMap = await GetCategoryIdMapAsync(connection, companyId);
        var accounts = new List<(int AccountId, string Code, string Name, string AccountType)>();
        await using (var command = new SqliteCommand(sql, connection))
        {
            command.Parameters.AddWithValue("company_id", companyId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                accounts.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        foreach (var account in accounts)
        {
            var role = InferRole(account.Name, account.AccountType);
            var categoryId = InferDefaultCategoryId(account.Name, account.AccountType, role, categoryMap);
            const string insertSql = @"
    INSERT INTO religious_report_account_roles (
        company_id, account_id, role, default_category_id, account_code_snapshot, account_name_snapshot
    )
    VALUES (@company_id, @account_id, @role, @default_category_id, @account_code_snapshot, @account_name_snapshot)";
            await using var command = new SqliteCommand(insertSql, connection);
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("account_id", account.AccountId);
            command.Parameters.AddWithValue("role", role);
            command.Parameters.AddWithValue("default_category_id", (object?)categoryId ?? DBNull.Value);
            command.Parameters.AddWithValue("account_code_snapshot", account.Code);
            command.Parameters.AddWithValue("account_name_snapshot", account.Name);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<Dictionary<string, long>> GetCategoryIdMapAsync(SqliteConnection connection, int companyId)
    {
        const string sql = "SELECT code, category_id FROM religious_report_categories WHERE company_id = @company_id";
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            map[reader.GetString(0)] = reader.GetInt64(1);
        }

        return map;
    }

    private static string InferRole(string accountName, string accountType)
    {
        if (ContainsAny(accountName, "現金", "預金", "当座", "小口")) return "cash";
        if (ContainsAny(accountName, "借入")) return "borrowing";
        if (ContainsAny(accountName, "預り", "預かり", "源泉", "仮受")) return "deposit";
        if (ContainsAny(accountName, "未払", "買掛")) return "payable";
        if (ContainsAny(accountName, "未収", "売掛")) return "receivable";
        if (accountType == "revenue") return "income";
        if (accountType == "expense") return "expense";
        return "excluded";
    }

    private static long? InferDefaultCategoryId(string accountName, string accountType, string role, IReadOnlyDictionary<string, long> categoryMap)
    {
        if (role == "borrowing") return categoryMap.GetValueOrDefault("I900");
        if (role != "income" && role != "expense") return null;

        var code = accountType == "revenue" ? "I990" : "E990";
        if (ContainsAny(accountName, "お布施", "布施", "献金")) code = "I010";
        else if (ContainsAny(accountName, "会費", "維持")) code = accountType == "revenue" ? "I020" : "E040";
        else if (ContainsAny(accountName, "寄付")) code = "I030";
        else if (ContainsAny(accountName, "墓地", "納骨")) code = "I040";
        else if (ContainsAny(accountName, "行事", "祭典", "法要")) code = accountType == "revenue" ? "I050" : "E050";
        else if (ContainsAny(accountName, "給与", "賞与", "人件")) code = "E020";
        else if (ContainsAny(accountName, "事務", "通信", "消耗", "雑費")) code = "E030";
        else if (ContainsAny(accountName, "修繕", "水道", "光熱", "管理")) code = "E040";
        else if (ContainsAny(accountName, "宗教", "布教", "教化")) code = "E010";

        return categoryMap.GetValueOrDefault(code);
    }

    private static string InferTreatment(string direction, string counterRole, long? categoryId)
    {
        if (counterRole == "deposit" || counterRole == "excluded") return "exclude";
        if (counterRole == "manual" || counterRole == "payable" || counterRole == "receivable") return "manual";
        if (counterRole == "borrowing") return direction == "income" ? "include" : "manual";
        if ((counterRole == "income" || counterRole == "expense") && categoryId.HasValue) return "include";
        return "manual";
    }

    private async Task<decimal> GetNetActualAsync(
        int companyId,
        DateTime periodStart,
        DateTime periodEnd,
        IReadOnlyList<ReligiousReportCategory> categories)
    {
        if (periodEnd < periodStart)
        {
            return 0;
        }

        var rows = await GetCashFlowReviewRowsAsync(companyId, periodStart, periodEnd);
        var actuals = GetActualsByCategory(rows);
        var categoryKinds = categories
            .Where(x => x.CategoryId.HasValue)
            .ToDictionary(x => x.CategoryId!.Value, x => x.Kind);

        decimal net = 0;
        foreach (var (categoryId, amount) in actuals)
        {
            net += categoryKinds.TryGetValue(categoryId, out var kind) && kind == "expense"
                ? -amount
                : amount;
        }

        return net;
    }

    private static Dictionary<long, decimal> GetActualsByCategory(IEnumerable<CashFlowReviewRow> rows)
    {
        return rows
            .Where(x => !x.IsChanged && x.EffectiveTreatment == "include" && x.EffectiveCategoryId.HasValue)
            .GroupBy(x => x.EffectiveCategoryId!.Value)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));
    }

    private static string BuildSourceSnapshot(
        DateTime entryDate,
        string entryNumber,
        string description,
        string direction,
        decimal amount,
        string cashAccountDisplay,
        string counterAccountDisplay)
    {
        return string.Join("|",
            entryDate.ToString("yyyy-MM-dd"),
            entryNumber,
            description,
            direction,
            amount.ToString("0.##", CultureInfo.InvariantCulture),
            cashAccountDisplay,
            counterAccountDisplay);
    }

    private static string BuildSourceHash(string sourceSnapshot)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourceSnapshot));
        return Convert.ToHexString(bytes);
    }

    private static decimal GetBudgetFactor(DateTime periodStart, DateTime periodEnd, DateTime fiscalStart, DateTime fiscalEnd)
    {
        if (periodStart <= fiscalStart && periodEnd >= fiscalEnd)
        {
            return 1m;
        }

        var start = periodStart < fiscalStart ? fiscalStart : periodStart;
        var end = periodEnd > fiscalEnd ? fiscalEnd : periodEnd;
        if (end < start)
        {
            return 0m;
        }

        var months = ((end.Year - start.Year) * 12) + end.Month - start.Month + 1;
        return Math.Clamp(months / 12m, 0m, 1m);
    }

    private static bool ContainsAny(string value, params string[] keywords)
    {
        return keywords.Any(value.Contains);
    }

    private static void ValidateCategory(ReligiousReportCategory category)
    {
        if (string.IsNullOrWhiteSpace(category.Code) || string.IsNullOrWhiteSpace(category.Name))
        {
            throw new InvalidOperationException("分類コードと分類名を入力してください。");
        }

        if (category.Kind is not ("income" or "expense"))
        {
            throw new InvalidOperationException("分類区分は income または expense を指定してください。");
        }
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
