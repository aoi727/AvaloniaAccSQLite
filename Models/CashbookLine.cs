namespace AccountingApp.Models;

public sealed record CashbookLine(
    long EntryId,
    DateTime EntryDate,
    string EntryNumber,
    string? Description,
    string? Reference,
    string? CounterpartAccountCode,
    string? CounterpartAccountName,
    string? CounterpartSubAccountCode,
    string? CounterpartSubAccountName,
    string? PartnerCode,
    string? PartnerName,
    string? InvoiceNumber,
    decimal Receipt,
    decimal Payment,
    decimal Balance);
