namespace ReligiousReportApp.Models;

public sealed record CompanyInfo(
    int CompanyId,
    string Name,
    DateTime FiscalYearStart,
    int ClosingDay);
