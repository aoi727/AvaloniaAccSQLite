using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<DashboardSummary> GetDashboardSummaryAsync(int companyId)
    {
        const string sql = @"
    SELECT
        (SELECT COUNT(*) FROM accounts WHERE company_id = @company_id) AS account_count,
        (SELECT COUNT(*) FROM sub_accounts WHERE company_id = @company_id AND is_active = TRUE) AS sub_account_count,
        (SELECT COUNT(*) FROM journal_vouchers WHERE company_id = @company_id) AS entry_count,
        COALESCE((SELECT SUM(amount) FROM journal_lines WHERE company_id = @company_id AND side = 'debit'), 0) AS total_entry_amount";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return new DashboardSummary(0, 0, 0, 0);
        }

        return new DashboardSummary(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetDecimal(3));
    }
}

