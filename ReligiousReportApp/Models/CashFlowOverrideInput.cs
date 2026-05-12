namespace ReligiousReportApp.Models;

public sealed record CashFlowOverrideInput(
    long CashLineId,
    long CounterLineId,
    string SourceHash,
    string SourceSnapshot,
    string Treatment,
    long? CategoryId,
    string? Note);
