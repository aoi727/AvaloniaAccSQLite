namespace AccountingApp.Models;

public sealed record UserCompanySummary(
    int CompanyId,
    string CompanyName,
    string Role)
{
    public override string ToString()
    {
        return CompanyName;
    }
}
