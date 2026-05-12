namespace ReligiousReportApp.Models;

public sealed record ReligiousReportSummary(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime FiscalYearStart,
    DateTime FiscalYearEnd,
    decimal BudgetFactor,
    int UnresolvedCount,
    decimal IncomeBudgetTotal,
    decimal IncomeActualTotal,
    decimal ExpenseBudgetTotal,
    decimal ExpenseActualTotal,
    decimal NetBudget,
    decimal NetActual,
    decimal FiscalOpeningCarryover,
    decimal PeriodOpeningCarryover,
    decimal ClosingCarryover,
    IReadOnlyList<ReligiousReportRow> Rows);
