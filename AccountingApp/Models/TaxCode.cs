namespace AccountingApp.Models;

public sealed record TaxCode(
    int TaxCodeId,
    string Code,
    string Name,
    string TaxKind,
    decimal TaxRate,
    bool IsPurchaseCredit,
    bool IsTaxable = true,
    bool RequiresInvoice = false,
    decimal DefaultPurchaseCreditRate = 0)
{
    public override string ToString()
    {
        return TaxRate == 0 ? Name : $"{Name} {TaxRate:0.##}%";
    }
}
