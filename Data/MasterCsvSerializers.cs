using System.Globalization;
using System.Text;

namespace AccountingApp.Data;

public sealed record AccountCsvRow(
    string Code,
    string Name,
    string AccountType,
    string BalanceSide,
    bool IsControlAccount,
    string? DefaultTaxCode,
    bool IsActive);

public sealed record SubAccountCsvRow(
    string AccountCode,
    string Code,
    string Name,
    string? ExternalCode,
    decimal Balance,
    bool IsActive);

internal static class AccountCsvSerializer
{
    private static readonly string[] Header =
    [
        "code",
        "name",
        "account_type",
        "balance_side",
        "is_control_account",
        "default_tax_code",
        "is_active"
    ];

    private static readonly HashSet<string> AccountTypes = ["asset", "liability", "equity", "revenue", "expense"];
    private static readonly HashSet<string> BalanceSides = ["debit", "credit"];

    public static string Serialize(IReadOnlyList<AccountCsvRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Header.Select(MasterCsvHelpers.Escape)));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",",
            [
                MasterCsvHelpers.Escape(row.Code),
                MasterCsvHelpers.Escape(row.Name),
                MasterCsvHelpers.Escape(row.AccountType),
                MasterCsvHelpers.Escape(row.BalanceSide),
                MasterCsvHelpers.Escape(row.IsControlAccount ? "true" : "false"),
                MasterCsvHelpers.Escape(row.DefaultTaxCode),
                MasterCsvHelpers.Escape(row.IsActive ? "true" : "false")
            ]));
        }

        return builder.ToString();
    }

    public static IReadOnlyList<AccountCsvRow> Deserialize(string csvText)
    {
        using var reader = new StringReader(csvText);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new InvalidOperationException("CSVヘッダーが見つかりません。");
        }

        var headerColumns = MasterCsvHelpers.ParseCsvLine(headerLine);
        if (!Header.SequenceEqual(headerColumns, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("勘定科目CSVのヘッダーが想定と異なります。アプリから出力したCSVを利用してください。");
        }

        var rows = new List<AccountCsvRow>();
        string? line;
        var lineNumber = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = MasterCsvHelpers.ParseCsvLine(line);
            if (columns.Count != Header.Length)
            {
                throw new InvalidOperationException($"CSV {lineNumber} 行目の列数が不正です。");
            }

            var accountType = MasterCsvHelpers.Require(columns[2], lineNumber, "account_type").ToLowerInvariant();
            if (!AccountTypes.Contains(accountType))
            {
                throw new InvalidOperationException($"CSV {lineNumber} 行目の account_type が不正です。");
            }

            var balanceSide = MasterCsvHelpers.Require(columns[3], lineNumber, "balance_side").ToLowerInvariant();
            if (!BalanceSides.Contains(balanceSide))
            {
                throw new InvalidOperationException($"CSV {lineNumber} 行目の balance_side が不正です。");
            }

            rows.Add(new AccountCsvRow(
                MasterCsvHelpers.Require(columns[0], lineNumber, "code"),
                MasterCsvHelpers.Require(columns[1], lineNumber, "name"),
                accountType,
                balanceSide,
                MasterCsvHelpers.ParseBool(columns[4], lineNumber, "is_control_account"),
                MasterCsvHelpers.NullIfEmpty(columns[5]),
                MasterCsvHelpers.ParseBool(columns[6], lineNumber, "is_active")));
        }

        return rows;
    }
}

internal static class SubAccountCsvSerializer
{
    private static readonly string[] Header =
    [
        "account_code",
        "code",
        "name",
        "external_code",
        "balance",
        "is_active"
    ];

    public static string Serialize(IReadOnlyList<SubAccountCsvRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Header.Select(MasterCsvHelpers.Escape)));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",",
            [
                MasterCsvHelpers.Escape(row.AccountCode),
                MasterCsvHelpers.Escape(row.Code),
                MasterCsvHelpers.Escape(row.Name),
                MasterCsvHelpers.Escape(row.ExternalCode),
                MasterCsvHelpers.Escape(row.Balance.ToString(CultureInfo.InvariantCulture)),
                MasterCsvHelpers.Escape(row.IsActive ? "true" : "false")
            ]));
        }

        return builder.ToString();
    }

    public static IReadOnlyList<SubAccountCsvRow> Deserialize(string csvText)
    {
        using var reader = new StringReader(csvText);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new InvalidOperationException("CSVヘッダーが見つかりません。");
        }

        var headerColumns = MasterCsvHelpers.ParseCsvLine(headerLine);
        if (!Header.SequenceEqual(headerColumns, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("補助科目CSVのヘッダーが想定と異なります。アプリから出力したCSVを利用してください。");
        }

        var rows = new List<SubAccountCsvRow>();
        string? line;
        var lineNumber = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = MasterCsvHelpers.ParseCsvLine(line);
            if (columns.Count != Header.Length)
            {
                throw new InvalidOperationException($"CSV {lineNumber} 行目の列数が不正です。");
            }

            rows.Add(new SubAccountCsvRow(
                MasterCsvHelpers.Require(columns[0], lineNumber, "account_code"),
                MasterCsvHelpers.Require(columns[1], lineNumber, "code"),
                MasterCsvHelpers.Require(columns[2], lineNumber, "name"),
                MasterCsvHelpers.NullIfEmpty(columns[3]),
                MasterCsvHelpers.ParseDecimal(columns[4], lineNumber, "balance"),
                MasterCsvHelpers.ParseBool(columns[5], lineNumber, "is_active")));
        }

        return rows;
    }
}

internal static class MasterCsvHelpers
{
    public static string Escape(string? value)
    {
        var text = EscapeNewlines(value ?? string.Empty);
        if (!text.Contains('"') && !text.Contains(',') && !text.Contains('\n') && !text.Contains('\r'))
        {
            return text;
        }

        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    public static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var current = line[i];
            if (current == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (current == ',' && !inQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(current);
        }

        values.Add(builder.ToString());
        return values;
    }

    public static string Require(string value, int lineNumber, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"CSV {lineNumber} 行目の {fieldName} は必須です。");
        }

        return RestoreNewlines(value.Trim());
    }

    public static string? NullIfEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : RestoreNewlines(value.Trim());
    }

    public static bool ParseBool(string value, int lineNumber, string fieldName)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => throw new InvalidOperationException($"CSV {lineNumber} 行目の {fieldName} は true / false で指定してください。")
        };
    }

    public static decimal ParseDecimal(string value, int lineNumber, string fieldName)
    {
        if (!decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
        {
            throw new InvalidOperationException($"CSV {lineNumber} 行目の {fieldName} が数値ではありません。");
        }

        return result;
    }

    private static string EscapeNewlines(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string RestoreNewlines(string value)
    {
        return value.Replace("\\n", "\n", StringComparison.Ordinal);
    }
}
