namespace AccountingApp.Models;

public sealed record TrialBalanceRow(
    int AccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    string BalanceSide,
    decimal PreviousBalance,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal EndingBalance);
