namespace AccountingApp.Models;

public sealed record JournalBookRow(
    long LineId,
    DateTime EntryDate,
    string EntryNumber,
    string? Description,
    string? Reference,
    string? DebitAccountDisplay,
    string? CreditAccountDisplay,
    decimal DebitAmount,
    decimal CreditAmount);
