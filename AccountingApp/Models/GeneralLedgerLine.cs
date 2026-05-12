namespace AccountingApp.Models;

public sealed record GeneralLedgerLine(
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
    decimal Debit,
    decimal Credit,
    decimal Balance);
