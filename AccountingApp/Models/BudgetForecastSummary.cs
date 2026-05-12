namespace AccountingApp.Models;

public sealed record BudgetForecastSummary(
    DateTime FiscalYearStart,
    DateTime FiscalYearEnd,
    DateTime ActualThrough,
    decimal OpeningCashBalance,
    decimal BudgetSalesTotal,
    decimal BudgetExpenseTotal,
    decimal BudgetProfitTotal,
    decimal ActualSalesToDate,
    decimal ActualExpensesToDate,
    decimal ActualProfitToDate,
    decimal LandingProfitTotal,
    decimal ProjectedEndingCash,
    IReadOnlyList<BudgetForecastMonthRow> Rows);
