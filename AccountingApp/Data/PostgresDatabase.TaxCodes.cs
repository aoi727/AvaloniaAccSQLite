using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<IReadOnlyList<TaxCode>> GetTaxCodesAsync(int companyId)
    {
        const string sql = @"
    SELECT tax_code_id, code, name, tax_kind, tax_rate, is_purchase_credit,
           COALESCE(is_taxable, tax_rate > 0) AS is_taxable,
           COALESCE(requires_invoice, FALSE) AS requires_invoice,
           COALESCE(default_purchase_credit_rate, 0) AS default_purchase_credit_rate
    FROM tax_codes
    WHERE company_id = @company_id
      AND is_active = TRUE
    ORDER BY code";

        var taxCodes = new List<TaxCode>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            taxCodes.Add(new TaxCode(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDecimal(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetDecimal(8)));
        }

        return taxCodes;
    }

    public async Task EnsureDefaultTaxCodesAsync(int companyId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        try
        {
            await InsertDefaultTaxCodesAsync(connection, transaction, companyId);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

