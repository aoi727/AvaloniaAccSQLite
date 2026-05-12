namespace ReligiousReportApp.Models;

public sealed record PeriodReviewStatus(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    string Status,
    string? Note);
