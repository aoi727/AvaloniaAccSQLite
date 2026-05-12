namespace AccountingApp.Models;

public sealed record BudgetPlanInput(
    DateTime MonthStart,
    decimal SalesBudget,
    decimal ExpenseBudget,
    decimal ExpectedCashIn,
    decimal ExpectedCashOut,
    string? Note);
