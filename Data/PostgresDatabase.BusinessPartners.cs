using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<IReadOnlyList<BusinessPartner>> GetBusinessPartnersAsync(int companyId)
    {
        const string sql = @"
    SELECT partner_id, code, name, partner_type, invoice_status, registration_number, is_active
    FROM business_partners
    WHERE company_id = @company_id
    ORDER BY code";

        var partners = new List<BusinessPartner>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            partners.Add(new BusinessPartner(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetBoolean(6)));
        }

        return partners;
    }

    public async Task<int> CreateBusinessPartnerAsync(
            int companyId,
            string code,
            string name,
            string partnerType,
            string invoiceStatus,
            string? registrationNumber,
            bool isActive)
    {
        const string sql = @"
    INSERT INTO business_partners (
        company_id, code, name, partner_type, invoice_status, registration_number, is_active
    )
    VALUES (
        @company_id, @code, @name, @partner_type, @invoice_status, @registration_number, @is_active
    )
    RETURNING partner_id";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        AddBusinessPartnerParameters(command, companyId, code, name, partnerType, invoiceStatus, registrationNumber, isActive);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<BusinessPartnerTransactionLine>> GetBusinessPartnerTransactionLinesAsync(
        int companyId,
        int partnerId,
        DateTime fromDate,
        DateTime toDate)
    {
        const string sql = @"
    WITH target_lines AS (
        SELECT l.line_id,
               l.voucher_id,
               v.entry_date,
               v.entry_number,
               a.code AS account_code,
               a.name AS account_name,
               s.code AS sub_account_code,
               s.name AS sub_account_name,
               l.description,
               v.reference,
               l.invoice_number,
               l.side,
               l.amount
        FROM journal_lines l
        JOIN journal_vouchers v ON v.voucher_id = l.voucher_id
        JOIN accounts a ON a.account_id = l.account_id
        LEFT JOIN sub_accounts s ON s.sub_account_id = NULLIF(l.sub_account_id, 0)
        WHERE l.company_id = @company_id
          AND l.partner_id = @partner_id
          AND v.entry_date >= @from_date
          AND v.entry_date <= @to_date
    ),
    counterpart_summary AS (
        SELECT cl.voucher_id,
               cl.side,
               COUNT(*) AS counterpart_count,
               MIN(a.code) AS account_code,
               MIN(a.name) AS account_name,
               MIN(s.code) AS sub_account_code,
               MIN(s.name) AS sub_account_name
        FROM journal_lines cl
        JOIN accounts a ON a.account_id = cl.account_id
        LEFT JOIN sub_accounts s ON s.sub_account_id = NULLIF(cl.sub_account_id, 0)
        GROUP BY cl.voucher_id, cl.side
    )
    SELECT t.line_id,
           t.entry_date,
           t.entry_number,
           t.account_code,
           t.account_name,
           t.sub_account_code,
           t.sub_account_name,
           CASE
               WHEN cp.counterpart_count = 1 THEN cp.account_code
               WHEN cp.counterpart_count > 1 THEN '複数'
               ELSE NULL
           END AS counterpart_account_code,
           CASE
               WHEN cp.counterpart_count = 1 THEN cp.account_name
               WHEN cp.counterpart_count > 1 THEN '複合仕訳'
               ELSE NULL
           END AS counterpart_account_name,
           CASE WHEN cp.counterpart_count = 1 THEN cp.sub_account_code ELSE NULL END AS counterpart_sub_account_code,
           CASE WHEN cp.counterpart_count = 1 THEN cp.sub_account_name ELSE NULL END AS counterpart_sub_account_name,
           t.description,
           t.reference,
           t.invoice_number,
           CASE WHEN t.side = 'debit' THEN t.amount ELSE 0 END AS debit_amount,
           CASE WHEN t.side = 'credit' THEN t.amount ELSE 0 END AS credit_amount
    FROM target_lines t
    LEFT JOIN counterpart_summary cp
      ON cp.voucher_id = t.voucher_id
     AND cp.side <> t.side
    ORDER BY t.entry_date, t.entry_number, t.line_id";

        var rows = new List<BusinessPartnerTransactionLine>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("partner_id", partnerId);
        command.Parameters.AddWithValue("from_date", fromDate.Date);
        command.Parameters.AddWithValue("to_date", toDate.Date);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new BusinessPartnerTransactionLine(
                reader.GetInt64(0),
                reader.GetDateTime(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetDecimal(14),
                reader.GetDecimal(15)));
        }

        return rows;
    }

    public async Task UpdateBusinessPartnerAsync(
            int companyId,
            int partnerId,
            string code,
            string name,
            string partnerType,
            string invoiceStatus,
            string? registrationNumber,
            bool isActive)
    {
        const string sql = @"
    UPDATE business_partners
    SET code = @code,
        name = @name,
        partner_type = @partner_type,
        invoice_status = @invoice_status,
        registration_number = @registration_number,
        is_active = @is_active,
        updated_at = CURRENT_TIMESTAMP
    WHERE company_id = @company_id
      AND partner_id = @partner_id";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        AddBusinessPartnerParameters(command, companyId, code, name, partnerType, invoiceStatus, registrationNumber, isActive);
        command.Parameters.AddWithValue("partner_id", partnerId);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddBusinessPartnerParameters(
        SqliteCommand command,
        int companyId,
        string code,
        string name,
        string partnerType,
        string invoiceStatus,
        string? registrationNumber,
        bool isActive)
    {
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("code", code.Trim());
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("partner_type", partnerType);
        command.Parameters.AddWithValue("invoice_status", invoiceStatus);
        command.Parameters.AddWithValue("registration_number", (object?)(NormalizeRegistrationNumber(registrationNumber)) ?? DBNull.Value);
        command.Parameters.AddWithValue("is_active", isActive);
    }

    private static string? NormalizeRegistrationNumber(string? registrationNumber)
    {
        if (string.IsNullOrWhiteSpace(registrationNumber))
        {
            return null;
        }

        var normalized = registrationNumber.Trim().Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
        return normalized.StartsWith('T') ? normalized : "T" + normalized;
    }
}
