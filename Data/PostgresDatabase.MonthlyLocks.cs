using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<MonthlyLockStatus> GetMonthlyLockStatusAsync(int companyId, DateTime targetDate)
    {
        var settings = await GetCompanySettingsAsync(companyId);
        var (periodStart, periodEnd) = GetMonthlyPeriodForDate(targetDate.Date, settings.ClosingDay);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await EnsureMonthlyLockSchemaAsync(connection, null);

        var lockRecord = await GetMonthlyLockAsync(connection, null, companyId, periodStart);
        return new MonthlyLockStatus(
            periodStart,
            periodEnd,
            lockRecord?.Status == "closed",
            lockRecord?.LockedAt,
            lockRecord?.UnlockReason,
            lockRecord?.UnlockedAt);
    }

    public async Task LockMonthlyPeriodAsync(int companyId, int userId, DateTime targetDate)
    {
        var settings = await GetCompanySettingsAsync(companyId);
        var (periodStart, periodEnd) = GetMonthlyPeriodForDate(targetDate.Date, settings.ClosingDay);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            await EnsureMonthlyLockSchemaAsync(connection, transaction);
            await EnsureOperationLogSchemaAsync(connection, transaction);

            var existing = await GetMonthlyLockAsync(connection, transaction, companyId, periodStart);
            if (existing?.Status == "closed")
            {
                throw new InvalidOperationException($"この月次は既にロック済みです。対象期間: {periodStart:yyyy/MM/dd} - {periodEnd:yyyy/MM/dd}");
            }

            const string sql = @"
    INSERT INTO monthly_locks (
        company_id, period_start, period_end, status, locked_by, locked_at, updated_at
    )
    VALUES (
        @company_id, @period_start, @period_end, 'closed', @locked_by, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
    )
    ON CONFLICT (company_id, period_start)
    DO UPDATE SET
        period_end = EXCLUDED.period_end,
        status = 'closed',
        locked_by = EXCLUDED.locked_by,
        locked_at = CURRENT_TIMESTAMP,
        unlocked_by = NULL,
        unlocked_at = NULL,
        unlock_reason = NULL,
        updated_at = CURRENT_TIMESTAMP";

            await using var command = new SqliteCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("period_start", periodStart);
            command.Parameters.AddWithValue("period_end", periodEnd);
            command.Parameters.AddWithValue("locked_by", userId);
            await command.ExecuteNonQueryAsync();

            await WriteOperationLogAsync(
                connection,
                transaction,
                companyId,
                userId,
                "monthly_lock",
                "monthly_lock",
                periodStart.ToString("yyyy-MM-dd"),
                $"月次をロックしました: {periodStart:yyyy/MM/dd} - {periodEnd:yyyy/MM/dd}");

            await transaction.CommitAsync();
            committed = true;
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

    public async Task UnlockMonthlyPeriodAsync(int companyId, int userId, DateTime targetDate, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("月次ロック解除の理由を入力してください。");
        }

        var settings = await GetCompanySettingsAsync(companyId);
        var (periodStart, periodEnd) = GetMonthlyPeriodForDate(targetDate.Date, settings.ClosingDay);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            await EnsureMonthlyLockSchemaAsync(connection, transaction);
            await EnsureOperationLogSchemaAsync(connection, transaction);

            var existing = await GetMonthlyLockAsync(connection, transaction, companyId, periodStart);
            if (existing?.Status != "closed")
            {
                throw new InvalidOperationException("解除できるロック済み月次が見つかりません。");
            }

            const string sql = @"
    UPDATE monthly_locks
    SET status = 'open',
        unlocked_by = @unlocked_by,
        unlocked_at = CURRENT_TIMESTAMP,
        unlock_reason = @unlock_reason,
        updated_at = CURRENT_TIMESTAMP
    WHERE company_id = @company_id
      AND period_start = @period_start";

            var normalizedReason = reason.Trim();
            await using var command = new SqliteCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("period_start", periodStart);
            command.Parameters.AddWithValue("unlocked_by", userId);
            command.Parameters.AddWithValue("unlock_reason", normalizedReason);
            await command.ExecuteNonQueryAsync();

            await WriteOperationLogAsync(
                connection,
                transaction,
                companyId,
                userId,
                "monthly_unlock",
                "monthly_lock",
                periodStart.ToString("yyyy-MM-dd"),
                $"月次ロックを解除しました: {periodStart:yyyy/MM/dd} - {periodEnd:yyyy/MM/dd}",
                "{\"reason\":\"" + normalizedReason.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");

            await transaction.CommitAsync();
            committed = true;
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

    private static async Task EnsureMonthlyLockSchemaAsync(SqliteConnection connection, SqliteTransaction? transaction)
    {
        const string sql = @"
    CREATE TABLE IF NOT EXISTS monthly_locks (
        monthly_lock_id INTEGER PRIMARY KEY AUTOINCREMENT,
        company_id      INTEGER NOT NULL REFERENCES companies(company_id),
        period_start    DATE NOT NULL,
        period_end      DATE NOT NULL,
        status          VARCHAR(20) NOT NULL DEFAULT 'open',
        locked_by       INTEGER REFERENCES users(user_id),
        locked_at       TIMESTAMP,
        unlocked_by     INTEGER REFERENCES users(user_id),
        unlocked_at     TIMESTAMP,
        unlock_reason   TEXT,
        created_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        updated_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
        UNIQUE(company_id, period_start),
        CHECK (status IN ('open','closed'))
    );
    CREATE INDEX IF NOT EXISTS idx_monthly_locks_company_period ON monthly_locks(company_id, period_start, period_end);
    CREATE INDEX IF NOT EXISTS idx_monthly_locks_company_status ON monthly_locks(company_id, status);";

        await using var command = transaction is null
            ? new SqliteCommand(sql, connection)
            : new SqliteCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<MonthlyLockRecord?> GetMonthlyLockAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        int companyId,
        DateTime periodStart)
    {
        const string sql = @"
    SELECT period_start, period_end, status, locked_at, unlocked_at, unlock_reason
    FROM monthly_locks
    WHERE company_id = @company_id
      AND period_start = @period_start";

        await using var command = transaction is null
            ? new SqliteCommand(sql, connection)
            : new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("period_start", periodStart.Date);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new MonthlyLockRecord(
            reader.GetDateTime(0),
            reader.GetDateTime(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetDateTime(3),
            reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    private static (DateTime PeriodStart, DateTime PeriodEnd) GetMonthlyPeriodForDate(DateTime targetDate, int closingDay)
    {
        var normalizedClosingDay = Math.Clamp(closingDay, 1, 31);
        var thisMonthClosing = CreateClosingDate(targetDate.Year, targetDate.Month, normalizedClosingDay);
        var periodEnd = targetDate.Date <= thisMonthClosing
            ? thisMonthClosing
            : CreateClosingDate(targetDate.AddMonths(1).Year, targetDate.AddMonths(1).Month, normalizedClosingDay);
        var previousClosing = CreateClosingDate(periodEnd.AddMonths(-1).Year, periodEnd.AddMonths(-1).Month, normalizedClosingDay);
        return (previousClosing.AddDays(1), periodEnd);
    }

    private static DateTime CreateClosingDate(int year, int month, int closingDay)
    {
        return new DateTime(year, month, Math.Min(closingDay, DateTime.DaysInMonth(year, month)));
    }

    private sealed record MonthlyLockRecord(
        DateTime PeriodStart,
        DateTime PeriodEnd,
        string Status,
        DateTime? LockedAt,
        DateTime? UnlockedAt,
        string? UnlockReason);
}
