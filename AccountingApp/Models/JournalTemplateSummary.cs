namespace AccountingApp.Models;

public sealed record JournalTemplateSummary(
    int TemplateId,
    string Name,
    bool IsSingleEntryMode,
    DateTime UpdatedAt)
{
    public override string ToString() => Name;
}
