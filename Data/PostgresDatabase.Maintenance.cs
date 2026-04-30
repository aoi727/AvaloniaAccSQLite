using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task ClearAllDataAsync()
    {
        const string sql = @"
    TRUNCATE TABLE
        sub_account_balances,
        journal_lines,
        journal_vouchers,
        business_partners,
        sub_accounts,
        accounts,
        tax_codes,
        user_companies,
        users,
        companies
    RESTART IDENTITY CASCADE";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
