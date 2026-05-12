namespace AccountingApp.Models;

public sealed record JournalVoucherAttachment(
    long? AttachmentId,
    string FileName,
    string? ContentType,
    byte[] Content);
