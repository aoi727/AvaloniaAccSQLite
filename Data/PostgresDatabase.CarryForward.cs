using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<AnnualCarryForwardStatus> GetAnnualCarryForwardStatusAsync(int companyId, DateTime today)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureAnnualCarryForwardSchemaAsync(connection, null);
        await EnsureAnnualClosingSchemaAsync(connection, null);

        var settings = await GetCompanySettingsAsync(companyId);
        var nextFiscalYearStart = GetFiscalYearStartForDate(settings.FiscalYearStart, today.Date);
        var sourceFiscalYearStart = ShiftFiscalYearStart(settings.FiscalYearStart, nextFiscalYearStart, -1);
        var sourceFiscalYearEnd = nextFiscalYearStart.AddDays(-1);

        var equityAccount = await FindCarryForwardEquityAccountAsync(connection, null, companyId);
        var execution = await GetAnnualCarryForwardExecutionAsync(connection, null, companyId, nextFiscalYearStart);
        var closing = await GetAnnualClosingAsync(connection, null, companyId, sourceFiscalYearStart);
        var netIncome = await GetCurrentPeriodNetIncomeAsync(companyId, sourceFiscalYearStart, sourceFiscalYearEnd);

        return new AnnualCarryForwardStatus(
            sourceFiscalYearStart,
            sourceFiscalYearEnd,
            nextFiscalYearStart,
            $"{equityAccount.Code} {equityAccount.Name}",
            netIncome,
            execution is not null,
            string.Equals(closing?.Status, "closed", StringComparison.OrdinalIgnoreCase),
            execution?.EntryNumber,
            execution?.CreatedAt,
            closing?.UnlockReason,
            closing?.UnlockedAt);
    }

    public async Task ExecuteAnnualCarryForwardAsync(int companyId, int userId, DateTime today)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            await EnsureAnnualCarryForwardSchemaAsync(connection, transaction);
            await EnsureAnnualClosingSchemaAsync(connection, transaction);
            await EnsureOperationLogSchemaAsync(connection, transaction);
            await EnsureDefaultSubAccountsAsync(connection, transaction, companyId);

            var settings = await GetCompanySettingsAsync(companyId);
            var nextFiscalYearStart = GetFiscalYearStartForDate(settings.FiscalYearStart, today.Date);
            var sourceFiscalYearStart = ShiftFiscalYearStart(settings.FiscalYearStart, nextFiscalYearStart, -1);
            var sourceFiscalYearEnd = nextFiscalYearStart.AddDays(-1);

            var existing = await GetAnnualCarryForwardExecutionAsync(connection, transaction, companyId, nextFiscalYearStart);
            if (existing is not null)
            {
                throw new InvalidOperationException($"年次繰越は実行済みです。伝票番号: {existing.EntryNumber}");
            }

            var equityAccount = await FindCarryForwardEquityAccountAsync(connection, transaction, companyId);
            var effectiveSubAccountId = await EnsureDefaultSubAccountAsync(connection, transaction, companyId, equityAccount.AccountId, equityAccount.Name);
            var closingLines = await GetCarryForwardClosingLinesAsync(connection, transaction, companyId, sourceFiscalYearStart, sourceFiscalYearEnd);

            if (closingLines.Count == 0)
            {
                throw new InvalidOperationException("繰越対象の損益残高がありません。");
            }

            var totalDebit = closingLines.Where(x => x.Side == "debit").Sum(x => x.Amount);
            var totalCredit = closingLines.Where(x => x.Side == "credit").Sum(x => x.Amount);
            var offsetAmount = Math.Abs(totalDebit - totalCredit);
            if (offsetAmount <= 0)
            {
                throw new InvalidOperationException("年次繰越の差額が計算できませんでした。");
            }

            var counterSide = totalDebit > totalCredit ? "credit" : "debit";
            closingLines.Add(new JournalLineInput(
                counterSide,
                equityAccount.AccountId,
                effectiveSubAccountId,
                offsetAmount,
                null,
                null,
                0,
                0,
                0,
                "excluded",
                "年次繰越"));

            var entryNumber = await GenerateCarryForwardEntryNumberAsync(connection, transaction, companyId, nextFiscalYearStart);
            var voucherId = await InsertJournalVoucherAsync(
                connection,
                transaction,
                companyId,
                entryNumber,
                nextFiscalYearStart,
                "年次繰越",
                userId);

            var lineNo = 1;
            foreach (var line in closingLines)
            {
                await InsertJournalLineAsync(connection, transaction, voucherId, companyId, lineNo++, line);
            }

            await InsertAnnualCarryForwardExecutionAsync(
                connection,
                transaction,
                companyId,
                sourceFiscalYearStart,
                sourceFiscalYearEnd,
                nextFiscalYearStart,
                entryNumber,
                equityAccount.AccountId,
                totalCredit - totalDebit,
                userId);

            await transaction.CommitAsync();
            committed = true;
            await RebuildSubAccountBalancesAsync(companyId);
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

    private static DateTime GetFiscalYearStartForDate(DateTime template, DateTime targetDate)
    {
        var year = targetDate.Month >= template.Month ? targetDate.Year : targetDate.Year - 1;
        var day = Math.Min(template.Day, DateTime.DaysInMonth(year, template.Month));
        return new DateTime(year, template.Month, day);
    }

    private static DateTime ShiftFiscalYearStart(DateTime template, DateTime currentFiscalYearStart, int years)
    {
        var year = currentFiscalYearStart.Year + years;
        var day = Math.Min(template.Day, DateTime.DaysInMonth(year, template.Month));
        return new DateTime(year, template.Month, day);
    }

    private static async Task EnsureAnnualCarryForwardSchemaAsync(SqliteConnection connection, SqliteTransaction? transaction)
    {
        const string sql = @"
    CREATE TABLE IF NOT EXISTS annual_carry_forwards (
        carry_forward_id         INTEGER PRIMARY KEY AUTOINCREMENT,
        company_id               INTEGER NOT NULL REFERENCES companies(company_id),
        source_fiscal_year_start DATE NOT NULL,
        source_fiscal_year_end   DATE NOT NULL,
        next_fiscal_year_start   DATE NOT NULL,
        entry_number             VARCHAR(30) NOT NULL,
        equity_account_id        INTEGER NOT NULL REFERENCES accounts(account_id),
        net_income               NUMERIC(15,2) NOT NULL,
        created_by               INTEGER REFERENCES users(user_id),
        created_at               TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(company_id, next_fiscal_year_start),
        UNIQUE(company_id, entry_number)
    );
    CREATE INDEX IF NOT EXISTS idx_annual_carry_forwards_company_start
        ON annual_carry_forwards(company_id, next_fiscal_year_start);";

        await using var command = transaction is null
            ? new SqliteCommand(sql, connection)
            : new SqliteCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<CarryForwardEquityAccount> FindCarryForwardEquityAccountAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        int companyId)
    {
        const string sql = @"
    SELECT account_id, code, name
    FROM accounts
    WHERE company_id = @company_id
      AND account_type = 'equity'
    ORDER BY
      CASE
          WHEN code = '8020' THEN 0
          WHEN name = '繰越利益剰余金' THEN 1
          WHEN name LIKE '%繰越利益%' THEN 2
          WHEN name LIKE '%利益剰余%' THEN 3
          WHEN name LIKE '%元入金%' THEN 4
          WHEN name LIKE '%資本金%' THEN 5
          ELSE 9
      END,
      code,
      name
    LIMIT 1";

        await using var command = transaction is null
            ? new SqliteCommand(sql, connection)
            : new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("年次繰越先の資本科目がありません。資本科目を登録してください。");
        }

        return new CarryForwardEquityAccount(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2));
    }

    private static async Task<CarryForwardExecution?> GetAnnualCarryForwardExecutionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        int companyId,
        DateTime nextFiscalYearStart)
    {
        const string sql = @"
    SELECT entry_number, created_at
    FROM annual_carry_forwards
    WHERE company_id = @company_id
      AND next_fiscal_year_start = @next_fiscal_year_start";

        await using var command = transaction is null
            ? new SqliteCommand(sql, connection)
            : new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("next_fiscal_year_start", (object?)(nextFiscalYearStart.Date) ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new CarryForwardExecution(
            reader.GetString(0),
            reader.GetDateTime(1));
    }

    private static async Task<int> EnsureDefaultSubAccountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId,
        int accountId,
        string accountName)
    {
        const string selectSql = @"
    SELECT sub_account_id
    FROM sub_accounts
    WHERE company_id = @company_id
      AND account_id = @account_id
      AND code = '0'
    LIMIT 1";

        await using (var selectCommand = new SqliteCommand(selectSql, connection, transaction))
        {
            selectCommand.Parameters.AddWithValue("company_id", companyId);
            selectCommand.Parameters.AddWithValue("account_id", accountId);
            var existing = await selectCommand.ExecuteScalarAsync();
            if (existing is not null && existing != DBNull.Value)
            {
                return Convert.ToInt32(existing);
            }
        }

        const string insertSql = @"
    INSERT INTO sub_accounts (company_id, account_id, code, name, balance, is_active)
    VALUES (@company_id, @account_id, '0', @name, 0, TRUE)
    RETURNING sub_account_id";

        await using var insertCommand = new SqliteCommand(insertSql, connection, transaction);
        insertCommand.Parameters.AddWithValue("company_id", companyId);
        insertCommand.Parameters.AddWithValue("account_id", accountId);
        insertCommand.Parameters.AddWithValue("name", accountName);
        var result = await insertCommand.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task<List<JournalLineInput>> GetCarryForwardClosingLinesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId,
        DateTime fromDate,
        DateTime toDate)
    {
        const string sql = @"
    WITH effective_lines AS (
        SELECT a.account_id,
               a.balance_side,
               COALESCE(NULLIF(l.sub_account_id, 0), default_sub.sub_account_id) AS effective_sub_account_id,
               SUM(
                   CASE
                       WHEN a.balance_side = 'debit' AND l.side = 'debit' THEN l.amount
                       WHEN a.balance_side = 'debit' AND l.side = 'credit' THEN -l.amount
                       WHEN a.balance_side = 'credit' AND l.side = 'credit' THEN l.amount
                       ELSE -l.amount
                   END
               ) AS balance_amount
        FROM journal_lines l
        JOIN journal_vouchers v ON v.voucher_id = l.voucher_id
        JOIN accounts a ON a.account_id = l.account_id
        LEFT JOIN sub_accounts default_sub
          ON default_sub.company_id = l.company_id
         AND default_sub.account_id = l.account_id
         AND default_sub.code = '0'
        WHERE l.company_id = @company_id
          AND v.entry_date >= @from_date
          AND v.entry_date <= @to_date
          AND a.account_type IN ('revenue', 'expense')
        GROUP BY a.account_id, a.balance_side, COALESCE(NULLIF(l.sub_account_id, 0), default_sub.sub_account_id)
    )
    SELECT account_id,
           balance_side,
           effective_sub_account_id,
           balance_amount
    FROM effective_lines
    WHERE effective_sub_account_id IS NOT NULL
      AND balance_amount <> 0
    ORDER BY account_id, effective_sub_account_id";

        var lines = new List<JournalLineInput>();
        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("from_date", (object?)(fromDate.Date) ?? DBNull.Value);
        command.Parameters.AddWithValue("to_date", (object?)(toDate.Date) ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var balanceSide = reader.GetString(1);
            var subAccountId = reader.GetInt32(2);
            var amount = reader.GetDecimal(3);
            var side = GetClosingSide(balanceSide, amount);

            lines.Add(new JournalLineInput(
                side,
                reader.GetInt32(0),
                subAccountId,
                Math.Abs(amount),
                null,
                null,
                0,
                0,
                0,
                "excluded",
                "年次繰越"));
        }

        return lines;
    }

    private static string GetClosingSide(string balanceSide, decimal balanceAmount)
    {
        var isPositive = balanceAmount > 0;
        return balanceSide switch
        {
            "debit" => isPositive ? "credit" : "debit",
            "credit" => isPositive ? "debit" : "credit",
            _ => throw new InvalidOperationException("残高性質が不正です。")
        };
    }

    private static async Task<string> GenerateCarryForwardEntryNumberAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId,
        DateTime entryDate)
    {
        var prefix = $"CF{entryDate:yyyyMMdd}-";
        const string sql = @"
    SELECT MAX(entry_number)
    FROM journal_vouchers
    WHERE company_id = @company_id
      AND entry_number LIKE @prefix";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("prefix", prefix + "%");
        var result = await command.ExecuteScalarAsync();
        var maxNumber = result == DBNull.Value ? null : Convert.ToString(result);

        var next = 1;
        if (!string.IsNullOrWhiteSpace(maxNumber) &&
            maxNumber.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(maxNumber[prefix.Length..], out var current))
        {
            next = current + 1;
        }

        return prefix + next.ToString("000");
    }

    private static async Task InsertAnnualCarryForwardExecutionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId,
        DateTime sourceFiscalYearStart,
        DateTime sourceFiscalYearEnd,
        DateTime nextFiscalYearStart,
        string entryNumber,
        int equityAccountId,
        decimal netIncome,
        int createdBy)
    {
        const string sql = @"
    INSERT INTO annual_carry_forwards (
        company_id,
        source_fiscal_year_start,
        source_fiscal_year_end,
        next_fiscal_year_start,
        entry_number,
        equity_account_id,
        net_income,
        created_by
    )
    VALUES (
        @company_id,
        @source_fiscal_year_start,
        @source_fiscal_year_end,
        @next_fiscal_year_start,
        @entry_number,
        @equity_account_id,
        @net_income,
        @created_by
    )";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("source_fiscal_year_start", (object?)(sourceFiscalYearStart.Date) ?? DBNull.Value);
        command.Parameters.AddWithValue("source_fiscal_year_end", (object?)(sourceFiscalYearEnd.Date) ?? DBNull.Value);
        command.Parameters.AddWithValue("next_fiscal_year_start", (object?)(nextFiscalYearStart.Date) ?? DBNull.Value);
        command.Parameters.AddWithValue("entry_number", entryNumber);
        command.Parameters.AddWithValue("equity_account_id", equityAccountId);
        command.Parameters.AddWithValue("net_income", netIncome);
        command.Parameters.AddWithValue("created_by", createdBy);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record CarryForwardEquityAccount(int AccountId, string Code, string Name);

    private sealed record CarryForwardExecution(string EntryNumber, DateTime CreatedAt);
}
