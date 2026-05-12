using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<IReadOnlyList<Account>> GetAccountsAsync(int companyId, bool includeInactive = false)
    {
        const string sql = @"
    SELECT account_id, code, name, account_type, balance_side, is_control_account, default_tax_code_id, is_active
    FROM accounts
    WHERE company_id = @company_id
      AND (@include_inactive OR is_active = TRUE)
    ORDER BY code";

        var accounts = new List<Account>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("include_inactive", includeInactive);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            accounts.Add(new Account(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.GetBoolean(7)));
        }

        return accounts;
    }

    public async Task<IReadOnlyList<Account>> GetControlAccountsAsync(int companyId)
    {
        const string sql = @"
    SELECT account_id, code, name, account_type, balance_side, is_control_account, default_tax_code_id, is_active
    FROM accounts
    WHERE company_id = @company_id
      AND is_control_account = TRUE
      AND is_active = TRUE
    ORDER BY code";

        var accounts = new List<Account>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            accounts.Add(new Account(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.GetBoolean(7)));
        }

        return accounts;
    }

    public async Task<int> CreateAccountAsync(
            int companyId,
            string code,
            string name,
            string accountType,
            string balanceSide,
            bool isControlAccount,
            int? defaultTaxCodeId)
    {
        const string sql = @"
    INSERT INTO accounts (company_id, code, name, account_type, balance_side, is_control_account, default_tax_code_id)
    VALUES (@company_id, @code, @name, @account_type, @balance_side, @is_control_account, @default_tax_code_id)
    RETURNING account_id";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            await using var command = new SqliteCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("code", code);
            command.Parameters.AddWithValue("name", name);
            command.Parameters.AddWithValue("account_type", accountType);
            command.Parameters.AddWithValue("balance_side", balanceSide);
            command.Parameters.AddWithValue("is_control_account", isControlAccount);
            command.Parameters.AddWithValue("default_tax_code_id", (object?)(defaultTaxCodeId) ?? DBNull.Value);

            var result = await command.ExecuteScalarAsync();
            var accountId = Convert.ToInt32(result);

            await InsertDefaultSubAccountAsync(connection, transaction, companyId, accountId, name);
            await transaction.CommitAsync();
            committed = true;

            await RebuildSubAccountBalancesAsync(companyId);
            return accountId;
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

    public async Task UpdateAccountAsync(
            int companyId,
            int accountId,
            string code,
            string name,
            string accountType,
            string balanceSide,
            bool isControlAccount,
            int? defaultTaxCodeId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        if (!isControlAccount && await HasSubAccountsAsync(connection, companyId, accountId))
        {
            throw new InvalidOperationException("補助科目が登録されているため、「補助科目あり」を外せません。");
        }

        const string sql = @"
    UPDATE accounts
        SET code = @code,
        name = @name,
        account_type = @account_type,
        balance_side = @balance_side,
        is_control_account = @is_control_account,
        default_tax_code_id = @default_tax_code_id
    WHERE company_id = @company_id
      AND account_id = @account_id";

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("account_type", accountType);
        command.Parameters.AddWithValue("balance_side", balanceSide);
        command.Parameters.AddWithValue("is_control_account", isControlAccount);
        command.Parameters.AddWithValue("default_tax_code_id", (object?)(defaultTaxCodeId) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
        await RebuildSubAccountBalancesAsync(companyId);
    }

    public async Task SetAccountActiveAsync(int companyId, int accountId, bool isActive)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        if (!isActive)
        {
            var reason = await GetAccountHideBlockReasonAsync(connection, companyId, accountId);
            if (reason is not null)
            {
                throw new InvalidOperationException(reason);
            }
        }

        const string sql = @"
    UPDATE accounts
    SET is_active = @is_active
    WHERE company_id = @company_id
      AND account_id = @account_id";

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("is_active", isActive);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<AccountCsvRow>> GetAccountCsvRowsAsync(int companyId)
    {
        const string sql = @"
    SELECT a.code,
           a.name,
           a.account_type,
           a.balance_side,
           a.is_control_account,
           t.code AS default_tax_code,
           a.is_active
    FROM accounts a
    LEFT JOIN tax_codes t
      ON t.tax_code_id = a.default_tax_code_id
    WHERE a.company_id = @company_id
    ORDER BY a.code";

        var rows = new List<AccountCsvRow>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new AccountCsvRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetBoolean(6)));
        }

        return rows;
    }

    public async Task ImportAccountCsvAsync(int companyId, IReadOnlyList<AccountCsvRow> rows)
    {
        if (rows.Count == 0)
        {
            throw new InvalidOperationException("インポートする勘定科目CSV行がありません。");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            var existingAccounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            const string existingSql = @"
    SELECT account_id, code
    FROM accounts
    WHERE company_id = @company_id";

            await using (var existingCommand = new SqliteCommand(existingSql, connection, transaction))
            {
                existingCommand.Parameters.AddWithValue("company_id", companyId);
                await using var reader = await existingCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    existingAccounts[reader.GetString(1)] = reader.GetInt32(0);
                }
            }

            var taxCodeMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            const string taxSql = @"
    SELECT code, tax_code_id
    FROM tax_codes
    WHERE company_id = @company_id";

            await using (var taxCommand = new SqliteCommand(taxSql, connection, transaction))
            {
                taxCommand.Parameters.AddWithValue("company_id", companyId);
                await using var reader = await taxCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    taxCodeMap[reader.GetString(0)] = reader.GetInt32(1);
                }
            }

            const string upsertSql = @"
    INSERT INTO accounts (
        company_id, code, name, account_type, balance_side, is_control_account, default_tax_code_id, is_active
    )
    VALUES (
        @company_id, @code, @name, @account_type, @balance_side, @is_control_account, @default_tax_code_id, @is_active
    )
    ON CONFLICT (company_id, code) DO UPDATE
    SET name = EXCLUDED.name,
        account_type = EXCLUDED.account_type,
        balance_side = EXCLUDED.balance_side,
        is_control_account = EXCLUDED.is_control_account,
        default_tax_code_id = EXCLUDED.default_tax_code_id,
        is_active = EXCLUDED.is_active
    RETURNING account_id";

            foreach (var row in rows)
            {
                if (existingAccounts.TryGetValue(row.Code, out var existingAccountId)
                    && !row.IsControlAccount
                    && await HasSubAccountsAsync(connection, companyId, existingAccountId))
                {
                    throw new InvalidOperationException($"補助科目があるため、勘定科目 {row.Code} を補助なしに変更できません。");
                }

                int? defaultTaxCodeId = null;
                if (!string.IsNullOrWhiteSpace(row.DefaultTaxCode))
                {
                    if (!taxCodeMap.TryGetValue(row.DefaultTaxCode, out var taxCodeId))
                    {
                        throw new InvalidOperationException($"既定税区分が見つかりません: {row.DefaultTaxCode}");
                    }

                    defaultTaxCodeId = taxCodeId;
                }

                await using var command = new SqliteCommand(upsertSql, connection, transaction);
                command.Parameters.AddWithValue("company_id", companyId);
                command.Parameters.AddWithValue("code", row.Code);
                command.Parameters.AddWithValue("name", row.Name);
                command.Parameters.AddWithValue("account_type", row.AccountType);
                command.Parameters.AddWithValue("balance_side", row.BalanceSide);
                command.Parameters.AddWithValue("is_control_account", row.IsControlAccount);
                command.Parameters.AddWithValue("default_tax_code_id", (object?)defaultTaxCodeId ?? DBNull.Value);
                command.Parameters.AddWithValue("is_active", row.IsActive);
                var accountId = Convert.ToInt32(await command.ExecuteScalarAsync());

                if (row.IsControlAccount)
                {
                    await InsertDefaultSubAccountAsync(connection, transaction, companyId, accountId, row.Name);
                }
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

    private static async Task<string?> GetAccountHideBlockReasonAsync(SqliteConnection connection, int companyId, int accountId)
    {
        const string usedSql = @"
    SELECT EXISTS (
        SELECT 1
        FROM journal_lines
        WHERE company_id = @company_id
          AND account_id = @account_id
    )";

        await using (var command = new SqliteCommand(usedSql, connection))
        {
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("account_id", accountId);
            if (Convert.ToBoolean(await command.ExecuteScalarAsync()))
            {
                return "過去の仕訳で使われているため、この勘定科目は非表示にできません。";
            }
        }

        const string balanceSql = @"
    SELECT EXISTS (
        SELECT 1
        FROM sub_accounts
        WHERE company_id = @company_id
          AND account_id = @account_id
          AND COALESCE(balance, 0) <> 0
    )
    OR EXISTS (
        SELECT 1
        FROM sub_account_balances sab
        JOIN sub_accounts sa ON sa.sub_account_id = sab.sub_account_id
        WHERE sab.company_id = @company_id
          AND sa.company_id = @company_id
          AND sa.account_id = @account_id
          AND COALESCE(sab.balance, 0) <> 0
    )";

        await using (var command = new SqliteCommand(balanceSql, connection))
        {
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("account_id", accountId);
            if (Convert.ToBoolean(await command.ExecuteScalarAsync()))
            {
                return "残高があるため、この勘定科目は非表示にできません。";
            }
        }

        return null;
    }

    private static async Task<bool> HasSubAccountsAsync(SqliteConnection connection, int companyId, int accountId)
    {
        const string sql = @"
    SELECT COUNT(*)
    FROM sub_accounts
    WHERE company_id = @company_id
      AND account_id = @account_id
      AND code <> '0'";

        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("account_id", accountId);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    private static async Task InsertDefaultSubAccountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int companyId,
        int accountId,
        string accountName)
    {
        const string sql = @"
    INSERT INTO sub_accounts (company_id, account_id, code, name, external_code, balance, is_active)
    VALUES (@company_id, @account_id, '0', @name, NULL, 0, TRUE)";

        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("name", accountName.Trim());
        await command.ExecuteNonQueryAsync();
    }
}

