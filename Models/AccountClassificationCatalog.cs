namespace AccountingApp.Models;

public enum FinancialStatementKind
{
    BalanceSheet,
    IncomeStatement,
    Other
}

public sealed record AccountClassification(
    int SortOrder,
    string Name,
    int RangeStart,
    int RangeEnd,
    FinancialStatementKind StatementKind,
    string StatementSection);

public sealed record AccountReportProfile(
    string AccountCode,
    string ClassificationName,
    int ClassificationSortOrder,
    FinancialStatementKind StatementKind,
    string StatementSection,
    int CodeSortValue,
    bool IsContraAccount);

public static class AccountClassificationCatalog
{
    private static readonly AccountClassification EquityClassification = new(
        110,
        "資本",
        8000,
        8999,
        FinancialStatementKind.BalanceSheet,
        "資本の部");

    private static readonly AccountClassification Unclassified = new(
        999,
        "未分類",
        int.MaxValue,
        int.MaxValue,
        FinancialStatementKind.Other,
        "未分類");

    private static readonly IReadOnlyList<AccountClassification> Classifications =
    [
        new(10, "現金・預金", 1001, 1499, FinancialStatementKind.BalanceSheet, "資産の部"),
        new(20, "当座資産", 1500, 1599, FinancialStatementKind.BalanceSheet, "資産の部"),
        new(30, "棚卸資産", 1600, 1699, FinancialStatementKind.BalanceSheet, "資産の部"),
        new(40, "その他流動資産", 1800, 1999, FinancialStatementKind.BalanceSheet, "資産の部"),
        new(50, "有形固定資産", 2000, 2399, FinancialStatementKind.BalanceSheet, "資産の部"),
        new(60, "無形固定資産", 2400, 2599, FinancialStatementKind.BalanceSheet, "資産の部"),
        new(70, "投資その他資産", 2600, 2899, FinancialStatementKind.BalanceSheet, "資産の部"),
        new(80, "繰延資産", 2900, 2999, FinancialStatementKind.BalanceSheet, "資産の部"),
        new(90, "流動負債", 3000, 3699, FinancialStatementKind.BalanceSheet, "負債の部"),
        new(100, "固定負債", 3700, 3799, FinancialStatementKind.BalanceSheet, "負債の部"),
        new(110, "資本金", 8008, 8008, FinancialStatementKind.BalanceSheet, "資本の部"),
        new(120, "売上高", 4000, 4299, FinancialStatementKind.IncomeStatement, "売上高"),
        new(130, "売上値引戻高", 4400, 4499, FinancialStatementKind.IncomeStatement, "売上原価"),
        new(140, "仕入高", 4500, 4999, FinancialStatementKind.IncomeStatement, "売上原価"),
        new(150, "材料仕入", 5000, 5099, FinancialStatementKind.IncomeStatement, "売上原価"),
        new(160, "労務費", 5100, 5199, FinancialStatementKind.IncomeStatement, "売上原価"),
        new(170, "外注費", 5200, 5299, FinancialStatementKind.IncomeStatement, "売上原価"),
        new(180, "製造経費", 5300, 5599, FinancialStatementKind.IncomeStatement, "売上原価"),
        new(190, "期首仕掛棚卸", 5900, 5969, FinancialStatementKind.IncomeStatement, "売上原価"),
        new(200, "期末仕掛棚卸", 5970, 5999, FinancialStatementKind.IncomeStatement, "売上原価"),
        new(210, "販売・一般管理", 6068, 6068, FinancialStatementKind.IncomeStatement, "販売費及び一般管理費"),
        new(220, "販売・一般管理", 6500, 6999, FinancialStatementKind.IncomeStatement, "販売費及び一般管理費"),
        new(230, "繰戻額等", 9000, 9499, FinancialStatementKind.IncomeStatement, "営業外収益・特別利益"),
        new(240, "繰入額等", 9500, 9799, FinancialStatementKind.IncomeStatement, "営業外費用・特別損失"),
        new(250, "諸口", 9999, 9999, FinancialStatementKind.Other, "諸口")
    ];

    public static IReadOnlyList<AccountClassification> GetOrderedClassifications()
    {
        return Classifications;
    }

    public static IReadOnlyList<AccountClassification> GetClassificationsFor(FinancialStatementKind statementKind)
    {
        return Classifications
            .Where(x => x.StatementKind == statementKind)
            .OrderBy(x => x.SortOrder)
            .ToList();
    }

    public static AccountClassification ResolveClassification(
        string accountCode,
        string? accountName = null,
        string? accountType = null)
    {
        if (string.Equals(accountType, "equity", StringComparison.OrdinalIgnoreCase))
        {
            return EquityClassification;
        }

        if (!TryParseCode(accountCode, out var numericCode))
        {
            return Unclassified;
        }

        return Classifications.FirstOrDefault(x => numericCode >= x.RangeStart && numericCode <= x.RangeEnd)
            ?? ResolveSpecialCode(numericCode, accountName)
            ?? Unclassified;
    }

    public static AccountReportProfile ResolveProfile(
        string accountCode,
        string? accountName = null,
        string? accountType = null,
        string? balanceSide = null)
    {
        var classification = ResolveClassification(accountCode, accountName, accountType);
        return new AccountReportProfile(
            accountCode,
            classification.Name,
            classification.SortOrder,
            classification.StatementKind,
            classification.StatementSection,
            GetCodeSortValue(accountCode),
            IsContraAccount(accountType, balanceSide));
    }

    private static AccountClassification? ResolveSpecialCode(int numericCode, string? accountName)
    {
        return numericCode switch
        {
            1984 => new AccountClassification(40, "その他流動資産", 1984, 1984, FinancialStatementKind.BalanceSheet, "資産の部"),
            9913 => new AccountClassification(40, "その他流動資産", 9913, 9913, FinancialStatementKind.BalanceSheet, "資産の部"),
            9920 => new AccountClassification(70, "投資その他資産", 9920, 9920, FinancialStatementKind.BalanceSheet, "資産の部"),
            9937 or 9944 => new AccountClassification(190, "期首仕掛棚卸", numericCode, numericCode, FinancialStatementKind.IncomeStatement, "売上原価"),
            9951 => new AccountClassification(200, "期末仕掛棚卸", 9951, 9951, FinancialStatementKind.IncomeStatement, "売上原価"),
            _ => null
        };
    }

    public static bool IsContraAccount(string? accountType, string? balanceSide)
    {
        if (string.IsNullOrWhiteSpace(accountType) || string.IsNullOrWhiteSpace(balanceSide))
        {
            return false;
        }

        var normalBalanceSide = accountType switch
        {
            "asset" => "debit",
            "expense" => "debit",
            "liability" => "credit",
            "equity" => "credit",
            "revenue" => "credit",
            _ => null
        };

        return normalBalanceSide is not null
            && !string.Equals(normalBalanceSide, balanceSide, StringComparison.OrdinalIgnoreCase);
    }

    public static decimal NormalizeBalanceForReports(decimal amount, bool isContraAccount)
    {
        return isContraAccount ? -amount : amount;
    }

    public static int GetCodeSortValue(string accountCode)
    {
        return TryParseCode(accountCode, out var numericCode) ? numericCode : int.MaxValue;
    }

    private static bool TryParseCode(string accountCode, out int numericCode)
    {
        return int.TryParse(accountCode, out numericCode);
    }
}
