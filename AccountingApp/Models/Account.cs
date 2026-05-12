namespace AccountingApp.Models;

public sealed record Account(
    int AccountId,
    string Code,
    string Name,
    string AccountType,
    string BalanceSide,
    bool IsControlAccount,
    int? DefaultTaxCodeId = null,
    bool IsActive = true)
{
    public override string ToString()
    {
        return $"{Code} {Name}";
    }
}
