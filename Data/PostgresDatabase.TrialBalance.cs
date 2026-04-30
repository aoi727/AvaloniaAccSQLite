using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<IReadOnlyList<TrialBalanceRow>> GetTrialBalanceRowsAsync(int companyId, DateTime fromDate, DateTime toDate)
    {
        var periodStart = fromDate.Date;
        var periodEnd = toDate.Date;
        if (periodEnd < periodStart)
        {
            throw new InvalidOperationException("終了日は開始日以降にしてください。");
        }

        var previousDate = periodStart.AddDays(-1);
        var fiscalYearStartMonth = await GetFiscalYearStartMonthAsync(companyId);

        const string sql = @"
    WITH balance_history AS (
        SELECT sab.sub_account_id,
               sab.balance,
               date(
                   printf('%04d-%02d-01',
                       CASE
                           WHEN sab.month >= @fiscal_year_start_month THEN sab.fiscal_year
                           ELSE sab.fiscal_year + 1
                       END,
                       sab.month),
                   '+1 month',
                   '-1 day') AS month_end
        FROM sub_account_balances sab
        WHERE sab.company_id = @company_id
    ),
    previous_snapshot AS (
        SELECT sa.sub_account_id,
               sa.account_id,
               sa.balance AS opening_balance,
               (
                   SELECT bh.balance
                   FROM balance_history bh
                   WHERE bh.sub_account_id = sa.sub_account_id
                     AND bh.month_end <= @previous_date
                   ORDER BY bh.month_end DESC
                   LIMIT 1
               ) AS snapshot_balance,
               (
                   SELECT bh.month_end
                   FROM balance_history bh
                   WHERE bh.sub_account_id = sa.sub_account_id
                     AND bh.month_end <= @previous_date
                   ORDER BY bh.month_end DESC
                   LIMIT 1
               ) AS snapshot_date
        FROM sub_accounts sa
        WHERE sa.company_id = @company_id
          AND sa.is_active = TRUE
    ),
    previous_changes AS (
        SELECT ps.sub_account_id,
               COALESCE((
                   SELECT SUM(
                       CASE
                           WHEN a.balance_side = 'debit' AND l.side = 'debit' THEN l.amount
                           WHEN a.balance_side = 'debit' AND l.side = 'credit' THEN -l.amount
                           WHEN a.balance_side = 'credit' AND l.side = 'credit' THEN l.amount
                           ELSE -l.amount
                       END
                   )
                   FROM journal_lines l
                   JOIN journal_vouchers v ON v.voucher_id = l.voucher_id
                   LEFT JOIN sub_accounts default_sub
                     ON default_sub.company_id = l.company_id
                    AND default_sub.account_id = l.account_id
                    AND default_sub.code = '0'
                   WHERE l.company_id = @company_id
                     AND COALESCE(NULLIF(l.sub_account_id, 0), default_sub.sub_account_id) = ps.sub_account_id
                     AND v.entry_date <= @previous_date
                     AND (ps.snapshot_date IS NULL OR v.entry_date > ps.snapshot_date)
               ), 0) AS bridge_change
        FROM previous_snapshot ps
        JOIN accounts a ON a.account_id = ps.account_id
    ),
    previous_balances AS (
        SELECT ps.account_id,
               SUM(COALESCE(ps.snapshot_balance, ps.opening_balance) + COALESCE(pc.bridge_change, 0)) AS previous_balance
        FROM previous_snapshot ps
        LEFT JOIN previous_changes pc ON pc.sub_account_id = ps.sub_account_id
        GROUP BY ps.account_id
    ),
    period_totals AS (
        SELECT l.account_id,
               SUM(CASE WHEN l.side = 'debit' THEN l.amount ELSE 0 END) AS debit_amount,
               SUM(CASE WHEN l.side = 'credit' THEN l.amount ELSE 0 END) AS credit_amount
        FROM journal_lines l
        JOIN journal_vouchers v ON v.voucher_id = l.voucher_id
        WHERE l.company_id = @company_id
          AND v.entry_date >= @from_date
          AND v.entry_date <= @to_date
        GROUP BY l.account_id
    )
    SELECT a.account_id,
           a.code,
           a.name,
           a.account_type,
           a.balance_side,
           COALESCE(pb.previous_balance, 0) AS previous_balance,
           COALESCE(pt.debit_amount, 0) AS debit_amount,
           COALESCE(pt.credit_amount, 0) AS credit_amount,
           CASE
               WHEN a.balance_side = 'debit'
                   THEN COALESCE(pb.previous_balance, 0) + COALESCE(pt.debit_amount, 0) - COALESCE(pt.credit_amount, 0)
               ELSE COALESCE(pb.previous_balance, 0) - COALESCE(pt.debit_amount, 0) + COALESCE(pt.credit_amount, 0)
           END AS ending_balance
    FROM accounts a
    LEFT JOIN previous_balances pb ON pb.account_id = a.account_id
    LEFT JOIN period_totals pt ON pt.account_id = a.account_id
    WHERE a.company_id = @company_id
    ORDER BY a.code, a.name";

        var rows = new List<TrialBalanceRow>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("fiscal_year_start_month", fiscalYearStartMonth);
        command.Parameters.AddWithValue("previous_date", (object?)(previousDate) ?? DBNull.Value);
        command.Parameters.AddWithValue("from_date", (object?)(periodStart) ?? DBNull.Value);
        command.Parameters.AddWithValue("to_date", (object?)(periodEnd) ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new TrialBalanceRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8)));
        }

        return rows
            .OrderBy(x => AccountClassificationCatalog.ResolveProfile(x.AccountCode, x.AccountName, x.AccountType, x.BalanceSide).ClassificationSortOrder)
            .ThenBy(x => AccountClassificationCatalog.ResolveProfile(x.AccountCode, x.AccountName, x.AccountType, x.BalanceSide).CodeSortValue)
            .ThenBy(x => x.AccountName, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<int> GetFiscalYearStartMonthAsync(int companyId)
    {
        const string sql = @"
    SELECT CAST(strftime('%m', fiscal_year_start) AS INTEGER)
    FROM companies
    WHERE company_id = @company_id";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        var result = await command.ExecuteScalarAsync();
        if (result is null || result == DBNull.Value)
        {
            throw new InvalidOperationException("会社情報が見つかりません。");
        }

        return Convert.ToInt32(result);
    }
}
