namespace AccountingApp.Models;

public sealed record BalanceSheetRow(
    int AccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    string BalanceSide,
    string ClassificationName,
    int ClassificationSortOrder,
    string StatementSection,
    decimal Balance)
{
    public bool IsContraAccount => AccountClassificationCatalog.IsContraAccount(AccountType, BalanceSide);

    public decimal ReportBalance => AccountClassificationCatalog.NormalizeBalanceForReports(Balance, IsContraAccount);
}
