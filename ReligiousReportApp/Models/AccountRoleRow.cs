namespace ReligiousReportApp.Models;

public sealed record AccountRoleRow(
    int AccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    string Role,
    long? DefaultCategoryId,
    string? DefaultCategoryCode,
    string? DefaultCategoryName);
