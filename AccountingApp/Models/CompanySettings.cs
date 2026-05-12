namespace AccountingApp.Models;

public sealed record CompanySettings(
    int CompanyId,
    string CompanyName,
    DateTime FiscalYearStart,
    int ClosingDay,
    string TaxEntryMethod,
    bool IsTaxExempt);
