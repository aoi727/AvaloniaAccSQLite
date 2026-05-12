namespace ReligiousReportApp.Models;

public sealed record AccountMappingRow(
    int AccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    long? CategoryId,
    string? CategoryCode,
    string? CategoryName,
    string? CategoryKind);
