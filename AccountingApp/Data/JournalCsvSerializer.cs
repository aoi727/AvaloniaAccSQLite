using System.Globalization;
using System.Text;
using AccountingApp.Models;

namespace AccountingApp.Data;

internal static class JournalCsvSerializer
{
    private static readonly string[] Header =
    [
        "entry_date",
        "entry_number",
        "reference",
        "side",
        "account_code",
        "sub_account_code",
        "amount",
        "tax_code",
        "tax_rate",
        "tax_amount",
        "creditable_tax_amount",
        "non_creditable_tax_amount",
        "tax_input_type",
        "partner_code",
        "invoice_number",
        "invoice_registration_number",
        "invoice_status",
        "purchase_credit_rate",
        "description"
    ];

    public static string Serialize(IReadOnlyList<JournalCsvRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Header.Select(Escape)));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",",
            [
                Escape(row.EntryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Escape(row.EntryNumber),
                Escape(row.Reference),
                Escape(row.Side),
                Escape(row.AccountCode),
                Escape(row.SubAccountCode),
                Escape(row.Amount.ToString(CultureInfo.InvariantCulture)),
                Escape(row.TaxCode),
                Escape(ToNullableString(row.TaxRate)),
                Escape(row.TaxAmount.ToString(CultureInfo.InvariantCulture)),
                Escape(row.CreditableTaxAmount.ToString(CultureInfo.InvariantCulture)),
                Escape(row.NonCreditableTaxAmount.ToString(CultureInfo.InvariantCulture)),
                Escape(row.TaxInputType),
                Escape(row.PartnerCode),
                Escape(row.InvoiceNumber),
                Escape(row.InvoiceRegistrationNumber),
                Escape(row.InvoiceStatus),
                Escape(ToNullableString(row.PurchaseCreditRate)),
                Escape(row.Description)
            ]));
        }

        return builder.ToString();
    }

    public static IReadOnlyList<JournalCsvRow> Deserialize(string csvText)
    {
        using var reader = new StringReader(csvText);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new InvalidOperationException("CSVヘッダーが見つかりません。");
        }

        var headerColumns = ParseCsvLine(headerLine);
        if (!Header.SequenceEqual(headerColumns, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CSVヘッダーが想定と異なります。アプリから出力したCSVを利用してください。");
        }

        var rows = new List<JournalCsvRow>();
        string? line;
        var lineNumber = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = ParseCsvLine(line);
            if (columns.Count != Header.Length)
            {
                throw new InvalidOperationException($"CSV {lineNumber} 行目の列数が不正です。");
            }

            rows.Add(new JournalCsvRow(
                ParseDate(columns[0], lineNumber),
                NullIfEmpty(columns[1]),
                NullIfEmpty(columns[2]),
                ParseSide(columns[3], lineNumber),
                Require(columns[4], lineNumber, "account_code"),
                NullIfEmpty(columns[5]),
                ParseDecimal(columns[6], lineNumber, "amount"),
                NullIfEmpty(columns[7]),
                ParseNullableDecimal(columns[8], lineNumber, "tax_rate"),
                ParseDecimal(columns[9], lineNumber, "tax_amount"),
                ParseDecimal(columns[10], lineNumber, "creditable_tax_amount"),
                ParseDecimal(columns[11], lineNumber, "non_creditable_tax_amount"),
                string.IsNullOrWhiteSpace(columns[12]) ? "none" : columns[12].Trim(),
                NullIfEmpty(columns[13]),
                NullIfEmpty(columns[14]),
                NullIfEmpty(columns[15]),
                NullIfEmpty(columns[16]),
                ParseNullableDecimal(columns[17], lineNumber, "purchase_credit_rate"),
                NullIfEmpty(columns[18])));
        }

        return rows;
    }

    private static string Escape(string? value)
    {
        var text = EscapeNewlines(value ?? string.Empty);
        if (!text.Contains('"') && !text.Contains(',') && !text.Contains('\n') && !text.Contains('\r'))
        {
            return text;
        }

        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static List<string> ParseCsvLine(string line)
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

    private static DateTime ParseDate(string value, int lineNumber)
    {
        var trimmed = value.Trim();
        var formats = new[]
        {
            "yyyy/M/d",
            "yyyy/MM/dd",
            "yyyy-M-d",
            "yyyy-MM-dd"
        };

        if (!DateTime.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
        {
            throw new InvalidOperationException($"CSV {lineNumber} 行目の entry_date が不正です。");
        }

        return result.Date;
    }

    private static decimal ParseDecimal(string value, int lineNumber, string fieldName)
    {
        if (!decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
        {
            throw new InvalidOperationException($"CSV {lineNumber} 行目の {fieldName} が数値ではありません。");
        }

        return result;
    }

    private static decimal? ParseNullableDecimal(string value, int lineNumber, string fieldName)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
        {
            throw new InvalidOperationException($"CSV {lineNumber} 行目の {fieldName} が数値ではありません。");
        }

        return result;
    }

    private static string ParseSide(string value, int lineNumber)
    {
        var side = value.Trim().ToLowerInvariant();
        if (side is not ("debit" or "credit"))
        {
            throw new InvalidOperationException($"CSV {lineNumber} 行目の side は debit か credit を指定してください。");
        }

        return side;
    }

    private static string Require(string value, int lineNumber, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"CSV {lineNumber} 行目の {fieldName} は必須です。");
        }

        return value.Trim();
    }

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : RestoreNewlines(value.Trim());
    }

    private static string? ToNullableString(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
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
