namespace AccountingApp.Models;

public sealed record TaxSummaryRow(
    string TaxKind,
    string TaxCode,
    string TaxName,
    decimal TaxRate,
    string TaxInputType,
    int LineCount,
    decimal GrossAmount,
    decimal NetAmount,
    decimal TaxAmount,
    decimal CreditableTaxAmount,
    decimal NonCreditableTaxAmount);
