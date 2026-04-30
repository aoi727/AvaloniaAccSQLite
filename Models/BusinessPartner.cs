namespace AccountingApp.Models;

public sealed record BusinessPartner(
    int PartnerId,
    string Code,
    string Name,
    string PartnerType,
    string InvoiceStatus,
    string? RegistrationNumber,
    bool IsActive)
{
    public override string ToString()
    {
        return $"{Code} {Name}";
    }
}
