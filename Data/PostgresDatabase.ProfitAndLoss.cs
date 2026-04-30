using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<ProfitAndLossSummary> GetProfitAndLossSummaryAsync(int companyId, DateTime fromDate, DateTime toDate)
    {
        var periodStart = fromDate.Date;
        var periodEnd = toDate.Date;
        if (periodEnd < periodStart)
        {
            throw new InvalidOperationException("終了日は開始日以降にしてください。");
        }

        const string sql = @"
    SELECT a.account_id,
           a.code,
           a.name,
           a.account_type,
           a.balance_side,
           COALESCE(SUM(
               CASE
                   WHEN a.balance_side = 'debit' AND l.side = 'debit' THEN l.amount
                   WHEN a.balance_side = 'debit' AND l.side = 'credit' THEN -l.amount
                   WHEN a.balance_side = 'credit' AND l.side = 'credit' THEN l.amount
                   ELSE -l.amount
               END
           ), 0) AS amount
    FROM accounts a
    LEFT JOIN (
        SELECT l.account_id,
               l.company_id,
               l.side,
               l.amount
        FROM journal_lines l
        JOIN journal_vouchers v ON v.voucher_id = l.voucher_id
        WHERE v.entry_date >= @from_date
          AND v.entry_date <= @to_date
    ) l
      ON l.account_id = a.account_id
     AND l.company_id = a.company_id
    WHERE a.company_id = @company_id
    GROUP BY a.account_id, a.code, a.name, a.account_type, a.balance_side
    ORDER BY a.code, a.name";

        var rows = new List<ProfitAndLossRow>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("from_date", (object?)(periodStart) ?? DBNull.Value);
        command.Parameters.AddWithValue("to_date", (object?)(periodEnd) ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var accountCode = reader.GetString(1);
            var accountName = reader.GetString(2);
            var accountType = reader.GetString(3);
            var balanceSide = reader.GetString(4);
            var profile = AccountClassificationCatalog.ResolveProfile(accountCode, accountName, accountType, balanceSide);
            if (profile.StatementKind != FinancialStatementKind.IncomeStatement)
            {
                continue;
            }

            rows.Add(new ProfitAndLossRow(
                reader.GetInt32(0),
                accountCode,
                accountName,
                accountType,
                balanceSide,
                profile.ClassificationName,
                profile.ClassificationSortOrder,
                profile.StatementSection,
                reader.GetDecimal(5)));
        }

        var orderedRows = rows
            .OrderBy(x => x.ClassificationSortOrder)
            .ThenBy(x => AccountClassificationCatalog.GetCodeSortValue(x.AccountCode))
            .ThenBy(x => x.AccountName, StringComparer.Ordinal)
            .ToList();

        var netSales = orderedRows
            .Where(x => x.StatementSection == "売上高")
            .Sum(x => x.ReportAmount);

        var costOfSales = orderedRows
            .Where(x => x.StatementSection == "売上原価")
            .Sum(x => x.ReportAmount);

        var grossProfit = netSales - costOfSales;

        var sga = orderedRows
            .Where(x => x.StatementSection == "販売費及び一般管理費")
            .Sum(x => x.ReportAmount);

        var operatingProfit = grossProfit - sga;

        var gains = orderedRows
            .Where(x => x.StatementSection == "営業外収益・特別利益")
            .Sum(x => x.ReportAmount);

        var losses = orderedRows
            .Where(x => x.StatementSection == "営業外費用・特別損失")
            .Sum(x => x.ReportAmount);

        var netIncome = operatingProfit + gains - losses;

        return new ProfitAndLossSummary(
            orderedRows,
            netSales,
            costOfSales,
            grossProfit,
            sga,
            operatingProfit,
            gains,
            losses,
            netIncome);
    }
}
