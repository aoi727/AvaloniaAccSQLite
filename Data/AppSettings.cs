using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AccountingApp.Data;

public static class AppSettings
{
    private const string ConnectionEnvironmentVariable = "ACCOUNTING_APP_CONNECTION";
    private const string SettingsFileName = "appsettings.json";

    public static string GetConnectionString()
    {
        var env = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        var configuredDatabasePath = GetConfiguredDatabasePath();
        if (!string.IsNullOrWhiteSpace(configuredDatabasePath))
        {
            return BuildSqliteConnectionString(configuredDatabasePath);
        }

        throw new InvalidOperationException(
            "SQLite の接続情報が設定されていません。" + Environment.NewLine +
            $"環境変数 {ConnectionEnvironmentVariable} を設定するか、{SettingsFileName} を作成してください。");
    }

    public static string BuildSqliteConnectionString(string databasePath)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();
    }

    public static string? GetConfiguredDatabasePath()
    {
        var env = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return TryGetDataSource(env, Environment.CurrentDirectory);
        }

        foreach (var path in GetCandidateSettingsPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var connection = ReadConnectionString(path);
            if (string.IsNullOrWhiteSpace(connection))
            {
                continue;
            }

            return TryGetDataSource(connection, Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
        }

        return null;
    }

    public static bool TryGetExistingConfiguredDatabase(out string databasePath)
    {
        databasePath = GetConfiguredDatabasePath() ?? "";
        return !string.IsNullOrWhiteSpace(databasePath) && File.Exists(databasePath);
    }

    public static void SaveDatabasePath(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var settingsPath = GetWritableSettingsPath();
        var directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new
        {
            ConnectionStrings = new
            {
                Default = BuildSqliteConnectionString(fullPath)
            }
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(settingsPath, json + Environment.NewLine);
    }

    private static IEnumerable<string> GetCandidateSettingsPaths()
    {
        yield return Path.Combine(Environment.CurrentDirectory, SettingsFileName);
        yield return Path.Combine(AppContext.BaseDirectory, SettingsFileName);
        yield return Path.Combine(Environment.CurrentDirectory, "AccountingApp", SettingsFileName);
    }

    private static string GetWritableSettingsPath()
    {
        var currentDirectorySettings = Path.Combine(Environment.CurrentDirectory, SettingsFileName);
        if (File.Exists(currentDirectorySettings))
        {
            return currentDirectorySettings;
        }

        var baseDirectorySettings = Path.Combine(AppContext.BaseDirectory, SettingsFileName);
        if (File.Exists(baseDirectorySettings))
        {
            return baseDirectorySettings;
        }

        return currentDirectorySettings;
    }

    private static string? ReadConnectionString(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings) &&
            connectionStrings.TryGetProperty("Default", out var defaultConnection))
        {
            return defaultConnection.GetString();
        }

        return null;
    }

    private static string? TryGetDataSource(string connectionString, string baseDirectory)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource))
            {
                return null;
            }

            return Path.GetFullPath(builder.DataSource, baseDirectory);
        }
        catch
        {
            return null;
        }
    }
}
