namespace AccountingApp.Models;

public sealed record OperationLogEntry(
    long LogId,
    DateTime OccurredAt,
    string? UserDisplayName,
    string OperationType,
    string TargetType,
    string? TargetKey,
    string Summary,
    string? MetadataJson);
