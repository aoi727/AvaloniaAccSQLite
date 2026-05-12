using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<IReadOnlyList<TaxSummaryRow>> GetTaxSummaryRowsAsync(int companyId, DateTime fromDate, DateTime toDate)
    {
        var periodStart = fromDate.Date;
        var periodEnd = toDate.Date;
        if (periodEnd < periodStart)
        {
            throw new InvalidOperationException("終了日は開始日以降にしてください。");
        }

        const string sql = @"
    SELECT tc.tax_kind,
           tc.code,
           tc.name,
           COALESCE(l.tax_rate, tc.tax_rate, 0) AS tax_rate,
           COALESCE(l.tax_input_type, 'none') AS tax_input_type,
           COUNT(*) AS line_count,
           SUM(CASE
                   WHEN COALESCE(l.tax_input_type, 'none') = 'excluded'
                       THEN l.amount + COALESCE(l.tax_amount, 0)
                   ELSE l.amount
               END) AS gross_amount,
           SUM(CASE
                   WHEN COALESCE(l.tax_input_type, 'none') = 'included'
                       THEN l.amount - COALESCE(l.tax_amount, 0)
                   ELSE l.amount
               END) AS net_amount,
           SUM(COALESCE(l.tax_amount, 0)) AS tax_amount,
           SUM(COALESCE(l.creditable_tax_amount, 0)) AS creditable_tax_amount,
           SUM(COALESCE(l.non_creditable_tax_amount, 0)) AS non_creditable_tax_amount
    FROM journal_lines l
    JOIN journal_vouchers v ON v.voucher_id = l.voucher_id
    JOIN tax_codes tc ON tc.tax_code_id = l.tax_code_id
    WHERE l.company_id = @company_id
      AND v.entry_date >= @from_date
      AND v.entry_date <= @to_date
      AND (
          COALESCE(l.tax_amount, 0) <> 0
          OR COALESCE(l.creditable_tax_amount, 0) <> 0
          OR COALESCE(l.non_creditable_tax_amount, 0) <> 0
      )
    GROUP BY tc.tax_kind, tc.code, tc.name, COALESCE(l.tax_rate, tc.tax_rate, 0), COALESCE(l.tax_input_type, 'none')
    ORDER BY
        CASE tc.tax_kind
            WHEN 'sales' THEN 1
            WHEN 'purchase' THEN 2
            WHEN 'non_taxable' THEN 3
            WHEN 'exempt' THEN 4
            WHEN 'out_of_scope' THEN 5
            ELSE 9
        END,
        tc.code,
        tax_input_type";

        var rows = new List<TaxSummaryRow>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("from_date", periodStart);
        command.Parameters.AddWithValue("to_date", periodEnd);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new TaxSummaryRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetString(4),
                Convert.ToInt32(reader.GetInt64(5)),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10)));
        }

        return rows;
    }
}
