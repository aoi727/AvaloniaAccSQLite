namespace AccountingApp.Models;

public sealed record JournalVoucherSummary(
    string EntryNumber,
    DateTime EntryDate,
    string? Description,
    string? Reference,
    string? DebitAccounts,
    string? CreditAccounts,
    decimal DebitTotal,
    decimal CreditTotal,
    int LineCount);
