namespace AccountingApp.Models;

public sealed record BusinessPartnerTransactionLine(
    long EntryId,
    DateTime EntryDate,
    string EntryNumber,
    string? AccountCode,
    string? AccountName,
    string? SubAccountCode,
    string? SubAccountName,
    string? CounterpartAccountCode,
    string? CounterpartAccountName,
    string? CounterpartSubAccountCode,
    string? CounterpartSubAccountName,
    string? Description,
    string? Reference,
    string? InvoiceNumber,
    decimal Debit,
    decimal Credit);
