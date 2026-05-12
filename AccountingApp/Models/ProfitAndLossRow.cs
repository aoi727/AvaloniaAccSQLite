namespace AccountingApp.Models;

public sealed record ProfitAndLossRow(
    int AccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    string BalanceSide,
    string ClassificationName,
    int ClassificationSortOrder,
    string StatementSection,
    decimal Amount)
{
    public bool IsContraAccount => AccountClassificationCatalog.IsContraAccount(AccountType, BalanceSide);

    public decimal ReportAmount => AccountClassificationCatalog.NormalizeBalanceForReports(Amount, IsContraAccount);
}
