using AccountingApp.Models;
using Microsoft.Data.Sqlite;
using System.Text;

namespace AccountingApp.Data;

public sealed partial class SqliteDatabase
{
    private static async Task<int> InsertCompanyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        DateTime fiscalYearStart,
        int closingDay,
        string taxEntryMethod = "gross",
        bool isTaxExempt = false)
    {
        const string sql = @"
    INSERT INTO companies (name, fiscal_year_start, closing_day, tax_entry_method, is_tax_exempt)
    VALUES (@name, @fiscal_year_start, @closing_day, @tax_entry_method, @is_tax_exempt)
    RETURNING company_id";
        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("fiscal_year_start", fiscalYearStart.Date);
        command.Parameters.AddWithValue("closing_day", closingDay);
        command.Parameters.AddWithValue("tax_entry_method", NormalizeTaxEntryMethod(taxEntryMethod, isTaxExempt));
        command.Parameters.AddWithValue("is_tax_exempt", isTaxExempt);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static string NormalizeTaxEntryMethod(string taxEntryMethod, bool isTaxExempt)
    {
        if (isTaxExempt)
        {
            return "gross";
        }

        return taxEntryMethod is "gross" or "net" ? taxEntryMethod : "gross";
    }

    private static async Task<int> InsertUserAsync(SqliteConnection connection, SqliteTransaction transaction, string loginId, string displayName, PasswordHash passwordHash)
    {
        const string sql = @"
    INSERT INTO users (login_id, display_name, password_hash, password_salt)
    VALUES (@login_id, @display_name, @password_hash, @password_salt)
    RETURNING user_id";
        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("login_id", loginId);
        command.Parameters.AddWithValue("display_name", displayName);
        command.Parameters.AddWithValue("password_hash", passwordHash.Hash);
        command.Parameters.AddWithValue("password_salt", passwordHash.Salt);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task InsertUserCompanyAsync(SqliteConnection connection, SqliteTransaction transaction, int userId, int companyId, string role)
    {
        const string sql = "INSERT INTO user_companies (user_id, company_id, role) VALUES (@user_id, @company_id, @role)";
        await using var command = new SqliteCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("company_id", companyId);
        command.Parameters.AddWithValue("role", role);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertDefaultAccountsAsync(SqliteConnection connection, SqliteTransaction transaction, int companyId)
    {
        var seeded = await TryInsertAccountsFromSeedCsvAsync(connection, transaction, companyId);
        if (seeded)
        {
            return;
        }

        var accounts = new[]
        {
            ("1010", "現金", "asset", "debit"),
            ("1120", "普通預金", "asset", "debit"),
            ("1200", "売掛金", "asset", "debit"),
            ("2000", "買掛金", "liability", "credit"),
            ("3000", "資本金", "equity", "credit"),
            ("4000", "売上高", "revenue", "credit"),
            ("5000", "仕入高", "expense", "debit"),
            ("6000", "旅費交通費", "expense", "debit")
        };

        const string sql = @"
    INSERT INTO accounts (company_id, code, name, account_type, balance_side, is_control_account)
    VALUES (@company_id, @code, @name, @account_type, @balance_side, TRUE)
    RETURNING account_id";

        const string subAccountSql = @"
    INSERT INTO sub_accounts (company_id, account_id, code, name, balance)
    VALUES (@company_id, @account_id, '0', @name, 0)";

        foreach (var account in accounts)
        {
            await using var command = new SqliteCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("code", account.Item1);
            command.Parameters.AddWithValue("name", account.Item2);
            command.Parameters.AddWithValue("account_type", account.Item3);
            command.Parameters.AddWithValue("balance_side", account.Item4);
            var accountId = Convert.ToInt32(await command.ExecuteScalarAsync());

            await using var subAccountCommand = new SqliteCommand(subAccountSql, connection, transaction);
            subAccountCommand.Parameters.AddWithValue("company_id", companyId);
            subAccountCommand.Parameters.AddWithValue("account_id", accountId);
            subAccountCommand.Parameters.AddWithValue("name", account.Item2);
            await subAccountCommand.ExecuteNonQueryAsync();
        }
    }

    private static async Task<bool> TryInsertAccountsFromSeedCsvAsync(SqliteConnection connection, SqliteTransaction transaction, int companyId)
    {
        var csvPath = ResolveDatabaseScriptPath("seed_accounts.csv");
        if (!File.Exists(csvPath))
        {
            return false;
        }

        var accounts = await LoadSeedAccountsAsync(csvPath);
        if (accounts.Count == 0)
        {
            return false;
        }

        const string accountSql = @"
    INSERT INTO accounts (
        company_id, code, name, account_type, balance_side, is_control_account, default_tax_code_id
    )
    VALUES (
        @company_id, @code, @name, @account_type, @balance_side, @is_control_account,
        (SELECT tax_code_id FROM tax_codes WHERE company_id = @company_id AND code = @tax_code_code)
    )
    ON CONFLICT (company_id, code) DO UPDATE
    SET name = EXCLUDED.name,
        account_type = EXCLUDED.account_type,
        balance_side = EXCLUDED.balance_side,
        is_control_account = EXCLUDED.is_control_account,
        default_tax_code_id = EXCLUDED.default_tax_code_id
    RETURNING account_id";

        const string subAccountSql = @"
    INSERT INTO sub_accounts (
        company_id, account_id, code, name, external_code, balance, is_active
    )
    VALUES (
        @company_id, @account_id, '0', @name, NULL, 0, TRUE
    )
    ON CONFLICT (company_id, account_id, code) DO UPDATE
    SET name = EXCLUDED.name,
        is_active = TRUE";

        foreach (var account in accounts)
        {
            await using var accountCommand = new SqliteCommand(accountSql, connection, transaction);
            accountCommand.Parameters.AddWithValue("company_id", companyId);
            accountCommand.Parameters.AddWithValue("code", account.Code);
            accountCommand.Parameters.AddWithValue("name", account.Name);
            accountCommand.Parameters.AddWithValue("account_type", account.AccountType);
            accountCommand.Parameters.AddWithValue("balance_side", account.BalanceSide);
            accountCommand.Parameters.AddWithValue("is_control_account", account.IsControlAccount);
            accountCommand.Parameters.AddWithValue("tax_code_code", account.TaxCodeCode);
            var accountId = Convert.ToInt32(await accountCommand.ExecuteScalarAsync());

            await using var subAccountCommand = new SqliteCommand(subAccountSql, connection, transaction);
            subAccountCommand.Parameters.AddWithValue("company_id", companyId);
            subAccountCommand.Parameters.AddWithValue("account_id", accountId);
            subAccountCommand.Parameters.AddWithValue("name", account.Name);
            await subAccountCommand.ExecuteNonQueryAsync();
        }

        return true;
    }

    private static async Task<List<SeedAccountRow>> LoadSeedAccountsAsync(string csvPath)
    {
        var rows = new List<SeedAccountRow>();
        var encodings = new[]
        {
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false),
            Encoding.GetEncoding(932)
        };

        string[] lines = [];
        foreach (var encoding in encodings)
        {
            lines = await File.ReadAllLinesAsync(csvPath, encoding);
            if (lines.Length > 1)
            {
                break;
            }
        }

        if (lines.Length <= 1)
        {
            return rows;
        }

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = ParseCsvLine(line);
            if (columns.Count < 9)
            {
                continue;
            }

            rows.Add(new SeedAccountRow(
                columns[2],
                columns[3],
                columns[4],
                bool.TryParse(columns[5], out var isControlAccount) && isControlAccount,
                MapTaxCode(columns[7]),
                string.IsNullOrWhiteSpace(columns[8])
                    ? (columns[4] is "asset" or "expense" ? "debit" : "credit")
                    : columns[8]));
        }

        return rows;
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

    private static string MapTaxCode(string? defaultTaxCodeId)
    {
        return defaultTaxCodeId?.Trim() switch
        {
            "1" => "SALES10",
            "3" => "PURCHASE10",
            "7" => "NONTAX",
            "9" => "OUT",
            _ => "OUT"
        };
    }

    private static async Task InsertDefaultTaxCodesAsync(SqliteConnection connection, SqliteTransaction transaction, int companyId)
    {
        var taxCodes = new[]
        {
            ("SALES10", "課税売上10%", "sales", 10m, false, true, false, 0m),
            ("SALES8", "課税売上8%", "sales", 8m, false, true, false, 0m),
            ("PURCHASE10", "課税仕入10%", "purchase", 10m, true, true, true, 100m),
            ("PURCHASE8", "課税仕入8%", "purchase", 8m, true, true, true, 100m),
            ("PURCHASE10_NC", "控除対象外仕入10%", "purchase", 10m, false, true, false, 0m),
            ("PURCHASE8_NC", "控除対象外仕入8%", "purchase", 8m, false, true, false, 0m),
            ("NONTAX", "非課税", "non_taxable", 0m, false, false, false, 0m),
            ("EXEMPT", "免税", "exempt", 0m, false, false, false, 0m),
            ("OUT", "対象外", "out_of_scope", 0m, false, false, false, 0m)
        };

        const string sql = @"
    INSERT INTO tax_codes (
        company_id, code, name, tax_kind, tax_rate, is_purchase_credit,
        is_taxable, requires_invoice, default_purchase_credit_rate
    )
    VALUES (
        @company_id, @code, @name, @tax_kind, @tax_rate, @is_purchase_credit,
        @is_taxable, @requires_invoice, @default_purchase_credit_rate
    )
    ON CONFLICT (company_id, code) DO UPDATE
    SET name = EXCLUDED.name,
        tax_kind = EXCLUDED.tax_kind,
        tax_rate = EXCLUDED.tax_rate,
        is_purchase_credit = EXCLUDED.is_purchase_credit,
        is_taxable = EXCLUDED.is_taxable,
        requires_invoice = EXCLUDED.requires_invoice,
        default_purchase_credit_rate = EXCLUDED.default_purchase_credit_rate";

        foreach (var taxCode in taxCodes)
        {
            await using var command = new SqliteCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("company_id", companyId);
            command.Parameters.AddWithValue("code", taxCode.Item1);
            command.Parameters.AddWithValue("name", taxCode.Item2);
            command.Parameters.AddWithValue("tax_kind", taxCode.Item3);
            command.Parameters.AddWithValue("tax_rate", taxCode.Item4);
            command.Parameters.AddWithValue("is_purchase_credit", taxCode.Item5);
            command.Parameters.AddWithValue("is_taxable", taxCode.Item6);
            command.Parameters.AddWithValue("requires_invoice", taxCode.Item7);
            command.Parameters.AddWithValue("default_purchase_credit_rate", taxCode.Item8);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed record SeedAccountRow(
        string Code,
        string Name,
        string AccountType,
        bool IsControlAccount,
        string TaxCodeCode,
        string BalanceSide);
}
