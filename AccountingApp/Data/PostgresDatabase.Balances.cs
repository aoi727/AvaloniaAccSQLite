using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task RebuildSubAccountBalancesAsync(int companyId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            await EnsureDefaultSubAccountsAsync(connection, transaction, companyId);
            var fiscalYearStart = await GetCompanyFiscalYearStartAsync(connection, transaction, companyId);
            var startMonth = new DateTime(fiscalYearStart.Year, fiscalYearStart.Month, 1);
            var lastMonth = await GetLastJournalMonthAsync(connection, transaction, companyId);

            await DeleteSubAccountBalancesAsync(connection, transaction, companyId);
            if (!lastMonth.HasValue || lastMonth.Value < startMonth)
            {
                await transaction.CommitAsync();
                committed = true;
                return;
            }

            var openingBalances = await GetOpeningBalancesAsync(connection, transaction, companyId);
            var monthlyChanges = await GetMonthlyBalanceChangesAsync(connection, transaction, companyId);
            var currentMonth = startMonth;

            while (currentMonth <= lastMonth.Value)
            {
                foreach (var openingBalance in openingBalances)
                {
                    var key = (openingBalance.SubAccountId, currentMonth.Year, currentMonth.Month);
                    if (monthlyChanges.TryGetValue(key, out var change))
                    {
                        openingBalance.RunningBalance += change;
                    }

                    await InsertSubAccountBalanceAsync(
                        connection,
                        transaction,
                        companyId,
                        openingBalance.SubAccountId,
                        GetFiscalYear(currentMonth, fiscalYearStart.Month),
                        currentMonth.Month,
                        openingBalance.RunningBalance);
                }

                currentMonth = currentMonth.AddMonths(1);
            }

            await transaction.CommitAsync();
            committed = true;
        }
        catch
        {
            if (!committed)
            {
                await transaction.RollbackAsync();
            }

            throw;
        }
    }

    private static async Task<DateTime> GetCompanyFiscalYearStartAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId)
    {
        const string sql = @"
    SELECT fiscal_year_start
    FROM companies
    WHERE company_id = @company_id";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        var result = await command.ExecuteScalarAsync();
        if (result is null || result == DBNull.Value)
        {
            throw new InvalidOperationException("会社情報が見つかりません。");
        }

        return ToDateTimeValue(result);
    }

    private static async Task EnsureDefaultSubAccountsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId)
    {
        const string sql = @"
    INSERT INTO sub_accounts (company_id, account_id, code, name, external_code, balance, is_active)
    SELECT a.company_id,
           a.account_id,
           '0',
           a.name,
           NULL,
           0,
           TRUE
    FROM accounts a
    WHERE a.company_id = @company_id
      AND NOT EXISTS (
          SELECT 1
          FROM sub_accounts s
          WHERE s.company_id = a.company_id
            AND s.account_id = a.account_id
            AND s.code = '0'
      )";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<DateTime?> GetLastJournalMonthAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId)
    {
        const string sql = @"
    SELECT MAX(entry_date)
    FROM journal_vouchers
    WHERE company_id = @company_id";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        var result = await command.ExecuteScalarAsync();
        if (result is null || result == DBNull.Value)
        {
            return null;
        }

        var date = ToDateTimeValue(result);
        return new DateTime(date.Year, date.Month, 1);
    }

    private static async Task DeleteSubAccountBalancesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId)
    {
        const string sql = "DELETE FROM sub_account_balances WHERE company_id = @company_id";
        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<RunningSubAccountBalance>> GetOpeningBalancesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId)
    {
        const string sql = @"
    SELECT sub_account_id, balance
    FROM sub_accounts
    WHERE company_id = @company_id
      AND is_active = TRUE
    ORDER BY sub_account_id";

        var balances = new List<RunningSubAccountBalance>();
        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            balances.Add(new RunningSubAccountBalance(
                reader.GetInt32(0),
                reader.GetDecimal(1)));
        }

        return balances;
    }

    private static async Task<Dictionary<(int SubAccountId, int Year, int Month), decimal>> GetMonthlyBalanceChangesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId)
    {
        const string sql = @"
    WITH line_changes AS (
        SELECT COALESCE(NULLIF(l.sub_account_id, 0), default_sub.sub_account_id) AS effective_sub_account_id,
               CAST(strftime('%Y', v.entry_date) AS INTEGER) AS year_value,
               CAST(strftime('%m', v.entry_date) AS INTEGER) AS month_value,
               CASE
                   WHEN a.balance_side = 'debit' AND l.side = 'debit' THEN l.amount
                   WHEN a.balance_side = 'debit' AND l.side = 'credit' THEN -l.amount
                   WHEN a.balance_side = 'credit' AND l.side = 'credit' THEN l.amount
                   ELSE -l.amount
               END AS balance_change
        FROM journal_lines l
        JOIN journal_vouchers v ON v.voucher_id = l.voucher_id
        JOIN accounts a ON a.account_id = l.account_id
        LEFT JOIN sub_accounts default_sub
          ON default_sub.company_id = l.company_id
         AND default_sub.account_id = l.account_id
         AND default_sub.code = '0'
        WHERE l.company_id = @company_id
    )
    SELECT effective_sub_account_id,
           year_value,
           month_value,
           SUM(balance_change) AS monthly_change
    FROM line_changes
    WHERE effective_sub_account_id IS NOT NULL
    GROUP BY effective_sub_account_id, year_value, month_value";

        var changes = new Dictionary<(int SubAccountId, int Year, int Month), decimal>();
        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            changes[(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2))] = reader.GetDecimal(3);
        }

        return changes;
    }

    private static async Task InsertSubAccountBalanceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId,
        int subAccountId,
        int fiscalYear,
        int month,
        decimal balance)
    {
        const string sql = @"
    INSERT INTO sub_account_balances (company_id, sub_account_id, fiscal_year, month, balance)
    VALUES (@company_id, @sub_account_id, @fiscal_year, @month, @balance)";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("sub_account_id", subAccountId);
        command.Parameters.AddWithValue("fiscal_year", fiscalYear);
        command.Parameters.AddWithValue("month", month);
        command.Parameters.AddWithValue("balance", balance);
        await command.ExecuteNonQueryAsync();
    }

    private static int GetFiscalYear(DateTime month, int fiscalYearStartMonth)
    {
        return month.Month >= fiscalYearStartMonth ? month.Year : month.Year - 1;
    }

    private static DateTime ToDateTimeValue(object value)
    {
        return value switch
        {
            DateTime dateTime => dateTime,
            DateOnly dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue),
            _ => Convert.ToDateTime(value)
        };
    }

    private sealed class RunningSubAccountBalance
    {
        public RunningSubAccountBalance(int subAccountId, decimal runningBalance)
        {
            SubAccountId = subAccountId;
            RunningBalance = runningBalance;
        }

        public int SubAccountId { get; }

        public decimal RunningBalance { get; set; }
    }
}
