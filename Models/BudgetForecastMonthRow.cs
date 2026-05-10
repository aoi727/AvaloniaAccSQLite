namespace AccountingApp.Models;

public sealed record BudgetForecastMonthRow(
    DateTime MonthStart,
    DateTime MonthEnd,
    decimal SalesBudget,
    decimal ExpenseBudget,
    decimal ExpectedCashIn,
    decimal ExpectedCashOut,
    string? Note,
    decimal ActualSales,
    decimal ActualExpenses,
    decimal ActualProfit,
    decimal BudgetProfit,
    decimal ProfitVariance,
    bool IsActualClosed,
    decimal LandingProfit,
    decimal CashMovement,
    decimal CashEndingBalance);
