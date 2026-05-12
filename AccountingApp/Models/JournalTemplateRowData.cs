namespace AccountingApp.Models;

public sealed record JournalTemplateRowData(
    int RowNo,
    string? Description,
    int? PartnerId,
    string? InvoiceNumber,
    int? DebitAccountId,
    int? DebitSubAccountId,
    int? DebitTaxCodeId,
    string DebitTaxInputType,
    decimal? DebitAmount,
    int? CreditAccountId,
    int? CreditSubAccountId,
    int? CreditTaxCodeId,
    string CreditTaxInputType,
    decimal? CreditAmount);
