using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(int companyId)
    {
        return await GetSubAccountsAsync(companyId, null);
    }

    public async Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(int companyId, int? accountId)
    {
        const string sql = @"
    SELECT s.sub_account_id, s.account_id, a.code, a.name, s.code, s.name,
           s.external_code, s.balance, s.is_active
    FROM sub_accounts s
    JOIN accounts a ON a.account_id = s.account_id
    WHERE s.company_id = @company_id
      AND (@account_id IS NULL OR s.account_id = @account_id)
    ORDER BY a.code, s.code";

        var subAccounts = new List<SubAccount>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("account_id", (object?)(accountId) ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            subAccounts.Add(new SubAccount(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetDecimal(7),
                reader.GetBoolean(8)));
        }

        return subAccounts;
    }

    public async Task<int> CreateSubAccountAsync(
            int companyId,
            int accountId,
            string code,
            string name,
            string? externalCode,
            decimal openingBalance)
    {
        const string sql = @"
    INSERT INTO sub_accounts (company_id, account_id, code, name, external_code, balance)
    VALUES (@company_id, @account_id, @code, @name, @external_code, @balance)
    RETURNING sub_account_id";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            await using var command = new SqliteCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("account_id", accountId);
            command.Parameters.AddWithValue("code", code);
            command.Parameters.AddWithValue("name", name);
            command.Parameters.AddWithValue("external_code", (object?)(string.IsNullOrWhiteSpace(externalCode) ? null : externalCode.Trim()) ?? DBNull.Value);
            command.Parameters.AddWithValue("balance", openingBalance);

            var result = await command.ExecuteScalarAsync();
            await transaction.CommitAsync();
            committed = true;
            await RebuildSubAccountBalancesAsync(companyId);
            return Convert.ToInt32(result);
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

    public async Task UpdateSubAccountAsync(
        int companyId,
        int subAccountId,
        int accountId,
        string code,
        string name,
        string? externalCode,
        decimal openingBalance,
        bool isActive)
    {
        const string sql = @"
    UPDATE sub_accounts
    SET account_id = @account_id,
        code = @code,
        name = @name,
        external_code = @external_code,
        balance = @balance,
        is_active = @is_active
    WHERE company_id = @company_id
      AND sub_account_id = @sub_account_id";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            await using var command = new SqliteCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("sub_account_id", subAccountId);
            command.Parameters.AddWithValue("account_id", accountId);
            command.Parameters.AddWithValue("code", code);
            command.Parameters.AddWithValue("name", name);
            command.Parameters.AddWithValue("external_code", (object?)(string.IsNullOrWhiteSpace(externalCode) ? null : externalCode.Trim()) ?? DBNull.Value);
            command.Parameters.AddWithValue("balance", openingBalance);
            command.Parameters.AddWithValue("is_active", isActive);
            await command.ExecuteNonQueryAsync();

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

    public async Task<IReadOnlyList<SubAccountCsvRow>> GetSubAccountCsvRowsAsync(int companyId)
    {
        const string sql = @"
    SELECT a.code,
           s.code,
           s.name,
           s.external_code,
           s.balance,
           s.is_active
    FROM sub_accounts s
    JOIN accounts a ON a.account_id = s.account_id
    WHERE s.company_id = @company_id
      AND s.code <> '0'
    ORDER BY a.code, s.code";

        var rows = new List<SubAccountCsvRow>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new SubAccountCsvRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetDecimal(4),
                reader.GetBoolean(5)));
        }

        return rows;
    }

    public async Task ImportSubAccountCsvAsync(int companyId, IReadOnlyList<SubAccountCsvRow> rows)
    {
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("インポートする補助科目CSV行がありません。");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            var accountMap = new Dictionary<string, (int AccountId, bool IsControlAccount)>(StringComparer.OrdinalIgnoreCase);
            const string accountSql = @"
    SELECT account_id, code, is_control_account
    FROM accounts
    WHERE company_id = @company_id";

            await using (var accountCommand = new SqliteCommand(accountSql, connection, transaction))
            {
                accountCommand.Parameters.AddWithValue("company_id", companyId);
                await using var reader = await accountCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    accountMap[reader.GetString(1)] = (reader.GetInt32(0), reader.GetBoolean(2));
                }
            }

            const string upsertSql = @"
    INSERT INTO sub_accounts (
        company_id, account_id, code, name, external_code, balance, is_active
    )
    VALUES (
        @company_id, @account_id, @code, @name, @external_code, @balance, @is_active
    )
    ON CONFLICT (company_id, account_id, code) DO UPDATE
    SET name = EXCLUDED.name,
        external_code = EXCLUDED.external_code,
        balance = EXCLUDED.balance,
        is_active = EXCLUDED.is_active";

            foreach (var row in rows)
            {
                if (string.Equals(row.Code, "0", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("補助科目CSVでは code 0 を取り込めません。");
                }

                if (!accountMap.TryGetValue(row.AccountCode, out var accountInfo))
                {
                    throw new InvalidOperationException($"主科目コードが見つかりません: {row.AccountCode}");
                }

                if (!accountInfo.IsControlAccount)
                {
                    throw new InvalidOperationException($"主科目 {row.AccountCode} は補助科目を持てません。");
                }

                await using var command = new SqliteCommand(upsertSql, connection, transaction);
                command.Parameters.AddWithValue("company_id", companyId);
                command.Parameters.AddWithValue("account_id", accountInfo.AccountId);
                command.Parameters.AddWithValue("code", row.Code);
                command.Parameters.AddWithValue("name", row.Name);
                command.Parameters.AddWithValue("external_code", (object?)row.ExternalCode ?? DBNull.Value);
                command.Parameters.AddWithValue("balance", row.Balance);
                command.Parameters.AddWithValue("is_active", row.IsActive);
                await command.ExecuteNonQueryAsync();
            }

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
}

