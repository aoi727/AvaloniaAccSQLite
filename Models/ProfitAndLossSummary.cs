namespace AccountingApp.Models;

public sealed record ProfitAndLossSummary(
    IReadOnlyList<ProfitAndLossRow> Rows,
    decimal NetSales,
    decimal CostOfSales,
    decimal GrossProfit,
    decimal SellingGeneralAdministrativeExpenses,
    decimal OperatingProfit,
    decimal NonOperatingAndSpecialGains,
    decimal NonOperatingAndSpecialLosses,
    decimal NetIncome);
