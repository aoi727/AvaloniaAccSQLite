namespace AccountingApp.Models;

public sealed record JournalTemplateDetail(
    int TemplateId,
    string Name,
    string? Reference,
    bool IsSingleEntryMode,
    IReadOnlyList<JournalTemplateRowData> Rows);
