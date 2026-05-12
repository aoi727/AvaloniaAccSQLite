namespace ReligiousReportApp.Models;

public sealed record CashFlowReviewRow(
    long CashLineId,
    long CounterLineId,
    long VoucherId,
    string EntryNumber,
    DateTime EntryDate,
    string Description,
    string Direction,
    decimal Amount,
    string CashAccountDisplay,
    string CounterAccountDisplay,
    string CounterRole,
    bool IsComposite,
    bool IsChanged,
    string SourceHash,
    string SourceSnapshot,
    string SuggestedTreatment,
    long? SuggestedCategoryId,
    string EffectiveTreatment,
    long? EffectiveCategoryId,
    string? Note);
