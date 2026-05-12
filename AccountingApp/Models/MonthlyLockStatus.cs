namespace AccountingApp.Models;

public sealed record MonthlyLockStatus(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    bool IsLocked,
    DateTime? LockedAt,
    string? UnlockReason,
    DateTime? UnlockedAt);
