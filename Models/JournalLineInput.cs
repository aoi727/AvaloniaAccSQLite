namespace AccountingApp.Models;

public sealed record JournalLineInput(
    string Side,
    int AccountId,
    int? SubAccountId,
    decimal Amount,
    int? TaxCodeId,
    decimal? TaxRate,
    decimal TaxAmount,
    decimal CreditableTaxAmount,
    decimal NonCreditableTaxAmount,
    string TaxInputType,
    string? Description,
    int? PartnerId = null,
    string? InvoiceNumber = null,
    string? InvoiceRegistrationNumber = null,
    string? InvoiceStatus = null,
    decimal? PurchaseCreditRate = null);
