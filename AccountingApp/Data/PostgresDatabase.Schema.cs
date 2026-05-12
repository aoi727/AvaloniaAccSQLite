using AccountingApp.Models;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    public async Task InitializeSchemaAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var schemaPath = ResolveDatabaseScriptPath("schema.sql");
        var sql = await File.ReadAllTextAsync(schemaPath);
        await using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> CanConnectAsync()
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveDatabaseScriptPath(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Database", fileName);
        if (File.Exists(path))
        {
            return path;
        }

        path = Path.Combine(Environment.CurrentDirectory, "Database", fileName);
        if (File.Exists(path))
        {
            return path;
        }

        throw new FileNotFoundException($"Database script not found: {fileName}");
    }
}

