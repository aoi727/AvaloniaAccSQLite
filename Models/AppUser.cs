namespace AccountingApp.Models;

public sealed record AppUser(
    int UserId,
    string LoginId,
    string DisplayName,
    int CompanyId,
    string CompanyName,
    string Role);
