namespace ReligiousReportApp.Models;

public sealed record ReligiousReportCategory(
    long? CategoryId,
    int CompanyId,
    string Code,
    string Name,
    string Kind,
    int DisplayOrder,
    bool IsActive);
