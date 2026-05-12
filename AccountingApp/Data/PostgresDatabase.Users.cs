using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task<IReadOnlyList<UserAccount>> GetUsersAsync(int companyId)
    {
        const string sql = @"
    SELECT u.user_id, u.login_id, u.display_name, uc.role, u.is_active
    FROM users u
    JOIN user_companies uc ON uc.user_id = u.user_id
    WHERE uc.company_id = @company_id
    ORDER BY u.login_id";

        var users = new List<UserAccount>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("company_id", companyId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(new UserAccount(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4)));
        }

        return users;
    }

    public async Task<int> CreateUserAsync(
            int companyId,
            string loginId,
            string displayName,
            string password,
            string role,
            bool isActive)
    {
        var passwordHash = PasswordHasher.Create(password);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        try
        {
            const string userSql = @"
    INSERT INTO users (login_id, display_name, password_hash, password_salt, is_active)
    VALUES (@login_id, @display_name, @password_hash, @password_salt, @is_active)
    RETURNING user_id";
            await using var userCommand = new SqliteCommand(userSql, connection, transaction);
            userCommand.Parameters.AddWithValue("login_id", loginId);
            userCommand.Parameters.AddWithValue("display_name", displayName);
            userCommand.Parameters.AddWithValue("password_hash", passwordHash.Hash);
            userCommand.Parameters.AddWithValue("password_salt", passwordHash.Salt);
            userCommand.Parameters.AddWithValue("is_active", isActive);
            var result = await userCommand.ExecuteScalarAsync();
            var userId = Convert.ToInt32(result);

            await InsertUserCompanyAsync(connection, transaction, userId, companyId, role);
            await transaction.CommitAsync();
            return userId;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task UpdateUserAsync(
            int companyId,
            int actingUserId,
            bool actingUserIsAdmin,
            int userId,
            string displayName,
            string role,
            bool isActive,
            string? newPassword,
            string? currentPassword)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        try
        {
            if (!actingUserIsAdmin && actingUserId != userId)
            {
                throw new InvalidOperationException("他のユーザーのパスワードは変更できません。");
            }

            if (!string.IsNullOrWhiteSpace(newPassword) && (!actingUserIsAdmin || actingUserId == userId))
            {
                const string passwordSql = @"
    SELECT password_hash, password_salt
    FROM users
    WHERE user_id = @user_id";
                await using var passwordCommand = new SqliteCommand(passwordSql, connection, transaction);
                passwordCommand.Parameters.AddWithValue("user_id", userId);
                await using var reader = await passwordCommand.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    throw new InvalidOperationException("変更対象のユーザーが見つかりません。");
                }

                var storedHash = reader.GetString(0);
                var storedSalt = reader.GetString(1);
                await reader.CloseAsync();

                if (string.IsNullOrWhiteSpace(currentPassword) || !PasswordHasher.Verify(currentPassword, storedHash, storedSalt))
                {
                    throw new InvalidOperationException("現在のパスワードが正しくありません。");
                }
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                const string userSql = @"
    UPDATE users
    SET display_name = @display_name,
        is_active = @is_active,
        updated_at = CURRENT_TIMESTAMP
    WHERE user_id = @user_id";
                await using var userCommand = new SqliteCommand(userSql, connection, transaction);
                userCommand.Parameters.AddWithValue("user_id", userId);
                userCommand.Parameters.AddWithValue("display_name", displayName);
                userCommand.Parameters.AddWithValue("is_active", isActive);
                await userCommand.ExecuteNonQueryAsync();
            }
            else
            {
                var passwordHash = PasswordHasher.Create(newPassword);
                const string userSql = @"
    UPDATE users
    SET display_name = @display_name,
        password_hash = @password_hash,
        password_salt = @password_salt,
        is_active = @is_active,
        updated_at = CURRENT_TIMESTAMP
    WHERE user_id = @user_id";
                await using var userCommand = new SqliteCommand(userSql, connection, transaction);
                userCommand.Parameters.AddWithValue("user_id", userId);
                userCommand.Parameters.AddWithValue("display_name", displayName);
                userCommand.Parameters.AddWithValue("password_hash", passwordHash.Hash);
                userCommand.Parameters.AddWithValue("password_salt", passwordHash.Salt);
                userCommand.Parameters.AddWithValue("is_active", isActive);
                await userCommand.ExecuteNonQueryAsync();
            }

            const string roleSql = @"
    UPDATE user_companies
    SET role = @role
    WHERE company_id = @company_id
      AND user_id = @user_id";
            await using var roleCommand = new SqliteCommand(roleSql, connection, transaction);
            roleCommand.Parameters.AddWithValue("company_id", companyId);
            roleCommand.Parameters.AddWithValue("user_id", userId);
            roleCommand.Parameters.AddWithValue("role", role);
            await roleCommand.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

