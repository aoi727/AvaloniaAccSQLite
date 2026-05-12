namespace AccountingApp.Models;

public sealed record DashboardSummary(
    long AccountCount,
    long SubAccountCount,
    long EntryCount,
    decimal TotalEntryAmount);
