using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<IReadOnlyList<OperationLogEntry>> GetOperationLogsAsync(int companyId, int limit = 200)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureOperationLogSchemaAsync(connection, null);

        const string sql = @"
    SELECT l.log_id,
           l.occurred_at,
           u.display_name,
           l.operation_type,
           l.target_type,
           l.target_key,
           l.summary,
           l.metadata_json
    FROM operation_logs l
    LEFT JOIN users u ON u.user_id = l.user_id
    WHERE l.company_id = @company_id
    ORDER BY l.occurred_at DESC, l.log_id DESC
    LIMIT @limit";

        var rows = new List<OperationLogEntry>();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("limit", Math.Max(1, limit));

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new OperationLogEntry(
                reader.GetInt64(0),
                reader.GetDateTime(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return rows;
    }

    public string? GetDatabaseFilePath()
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder(_connectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource))
            {
                return null;
            }

            return Path.GetFullPath(builder.DataSource);
        }
        catch
        {
            return null;
        }
    }

    public async Task BackupDatabaseAsync(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new InvalidOperationException("バックアップ先のファイルを指定してください。");
        }

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(fullDestinationPath))
        {
            File.Delete(fullDestinationPath);
        }

        await using var sourceConnection = new SqliteConnection(_connectionString);
        await using var destinationConnection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = fullDestinationPath
            }.ToString());

        await sourceConnection.OpenAsync();
        await destinationConnection.OpenAsync();
        sourceConnection.BackupDatabase(destinationConnection);
    }

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
