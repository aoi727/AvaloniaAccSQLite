namespace AccountingApp.Models;

public sealed record SubAccount(
    int SubAccountId,
    int AccountId,
    string AccountCode,
    string AccountName,
    string Code,
    string Name,
    string? ExternalCode,
    decimal Balance,
    bool IsActive)
{
    public override string ToString()
    {
        return $"{Code} {Name}";
    }
}
