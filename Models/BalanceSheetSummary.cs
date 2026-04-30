namespace AccountingApp.Models;

public sealed record BalanceSheetSummary(
    IReadOnlyList<BalanceSheetRow> Rows,
    decimal CurrentPeriodNetIncome);
