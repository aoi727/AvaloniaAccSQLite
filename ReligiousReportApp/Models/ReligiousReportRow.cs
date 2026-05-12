namespace ReligiousReportApp.Models;

public sealed record ReligiousReportRow(
    string Kind,
    string CategoryCode,
    string CategoryName,
    decimal BudgetAmount,
    decimal ActualAmount,
    decimal VarianceAmount);
