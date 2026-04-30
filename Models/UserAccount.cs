namespace AccountingApp.Models;

public sealed record UserAccount(
    int UserId,
    string LoginId,
    string DisplayName,
    string Role,
    bool IsActive);
