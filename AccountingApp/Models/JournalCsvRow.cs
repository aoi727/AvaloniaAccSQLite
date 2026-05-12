namespace AccountingApp.Models;

public sealed record JournalCsvRow(
    DateTime EntryDate,
    string? EntryNumber,
    string? Reference,
    string Side,
    string AccountCode,
    string? SubAccountCode,
    decimal Amount,
    string? TaxCode,
    decimal? TaxRate,
    decimal TaxAmount,
    decimal CreditableTaxAmount,
    decimal NonCreditableTaxAmount,
    string TaxInputType,
    string? PartnerCode,
    string? InvoiceNumber,
    string? InvoiceRegistrationNumber,
    string? InvoiceStatus,
    decimal? PurchaseCreditRate,
    string? Description);
