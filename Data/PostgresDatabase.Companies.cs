using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<CompanySettings> GetCompanySettingsAsync(int companyId)
    {
        const string sql = @"
    SELECT company_id, name, fiscal_year_start, closing_day, tax_entry_method, is_tax_exempt
    FROM companies
    WHERE company_id = @company_id";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("会社情報が見つかりません。");
        }

        return new CompanySettings(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetDateTime(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetBoolean(5));
    }

    public async Task UpdateCompanyClosingDayAsync(int companyId, int closingDay)
    {
        var settings = await GetCompanySettingsAsync(companyId);
        await UpdateCompanySettingsAsync(
            companyId,
            settings.CompanyName,
            settings.FiscalYearStart,
            closingDay,
            settings.TaxEntryMethod,
            settings.IsTaxExempt);
    }

    public async Task UpdateCompanySettingsAsync(
        int companyId,
        string companyName,
        DateTime fiscalYearStart,
        int closingDay,
        string taxEntryMethod,
        bool isTaxExempt)
    {
        if (string.IsNullOrWhiteSpace(companyName))
        {
            throw new InvalidOperationException("会社名を入力してください。");
        }

        if (closingDay is < 1 or > 31)
        {
            throw new InvalidOperationException("締め日は1日から31日の範囲で入力してください。");
        }

        if (taxEntryMethod is not ("gross" or "net"))
        {
            throw new InvalidOperationException("消費税の記帳方式が不正です。");
        }

        var normalizedTaxEntryMethod = isTaxExempt ? "gross" : taxEntryMethod;

        const string sql = @"
    UPDATE companies
    SET name = @name,
        fiscal_year_start = @fiscal_year_start,
        closing_day = @closing_day,
        tax_entry_method = @tax_entry_method,
        is_tax_exempt = @is_tax_exempt
    WHERE company_id = @company_id";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("name", companyName.Trim());
        command.Parameters.AddWithValue("fiscal_year_start", fiscalYearStart.Date);
        command.Parameters.AddWithValue("closing_day", closingDay);
        command.Parameters.AddWithValue("tax_entry_method", normalizedTaxEntryMethod);
        command.Parameters.AddWithValue("is_tax_exempt", isTaxExempt);
        await command.ExecuteNonQueryAsync();
    }
}
