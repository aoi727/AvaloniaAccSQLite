using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<bool> HasUsersAsync()
    {
        const string sql = "SELECT COUNT(*) FROM users";
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    public async Task<AppUser?> AuthenticateAsync(string loginId, string password)
    {
        const string sql = @"
    SELECT u.user_id, u.login_id, u.display_name, u.password_hash, u.password_salt
    FROM users u
    WHERE u.login_id = @login_id
      AND u.is_active = TRUE
    LIMIT 1";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("login_id", loginId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var userId = reader.GetInt32(0);
        var userLoginId = reader.GetString(1);
        var displayName = reader.GetString(2);
        var hash = reader.GetString(3);
        var salt = reader.GetString(4);
        if (!PasswordHasher.Verify(password, hash, salt))
        {
            return null;
        }

        var companyCount = await GetCompanyCountAsync();
        if (companyCount > 1)
        {
            throw new InvalidOperationException("SQLite版では1つのDBにつき会社は1社のみです。別会社は別のDBファイルに分けてください。");
        }

        var companies = await GetUserCompaniesAsync(userId);
        var company = companies.FirstOrDefault()
            ?? throw new InvalidOperationException("利用可能な会社が割り当てられていません。");

        return new AppUser(
            userId,
            userLoginId,
            displayName,
            company.CompanyId,
            company.CompanyName,
            company.Role);
    }

    public async Task<AppUser> CreateInitialAdminAsync(
        string companyName,
        DateTime fiscalYearStart,
        int closingDay,
        string loginId,
        string displayName,
        string password,
        string taxEntryMethod = "gross",
        bool isTaxExempt = false,
        string? accountSeedCsvPath = null)
    {
        var passwordHash = PasswordHasher.Create(password);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        var committed = false;

        try
        {
            var companyId = await InsertCompanyAsync(connection, transaction, companyName, fiscalYearStart, closingDay, taxEntryMethod, isTaxExempt);
            var userId = await InsertUserAsync(connection, transaction, loginId, displayName, passwordHash);
            await InsertUserCompanyAsync(connection, transaction, userId, companyId, "admin");
            await InsertDefaultTaxCodesAsync(connection, transaction, companyId);
            await InsertDefaultAccountsAsync(connection, transaction, companyId, accountSeedCsvPath);
            await transaction.CommitAsync();
            committed = true;
            return new AppUser(userId, loginId, displayName, companyId, companyName, "admin");
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

    public async Task<int> GetCompanyClosingDayAsync(int companyId)
    {
        const string sql = @"
    SELECT closing_day
    FROM companies
    WHERE company_id = @company_id";

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? 31 : Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<UserCompanySummary>> GetUserCompaniesAsync(int userId)
    {
        const string sql = @"
    SELECT c.company_id, c.name, uc.role
    FROM user_companies uc
    JOIN companies c ON c.company_id = uc.company_id
    WHERE uc.user_id = @user_id
    ORDER BY c.company_id";

        var companies = new List<UserCompanySummary>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            companies.Add(new UserCompanySummary(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return companies;
    }

    private async Task<int> GetCompanyCountAsync()
    {
        const string sql = "SELECT COUNT(*) FROM companies";
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
}
