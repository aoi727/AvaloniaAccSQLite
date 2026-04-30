namespace AccountingApp.Models;

public sealed record AnnualCarryForwardStatus(
    DateTime SourceFiscalYearStart,
    DateTime SourceFiscalYearEnd,
    DateTime NextFiscalYearStart,
    string EquityAccountDisplayName,
    decimal NetIncome,
    bool AlreadyExecuted,
    bool IsClosed,
    string? EntryNumber,
    DateTime? ExecutedAt,
    string? UnlockReason,
    DateTime? UnlockedAt);
