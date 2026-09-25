namespace Admin.Auth;

/// <summary>Which sign-in tab an account belongs to. Only vendors can reset their own password.</summary>
public enum AccountType { Employee, Vendor }

/// <summary>What the auth module needs to know about a user. Map it from your own user table.</summary>
/// <param name="Id">Unique across employees AND vendors, e.g. "E:1042" / "V:88", because sessions look users up by id only.</param>
/// <param name="SecurityStamp">Random value that changes whenever credentials change; a mismatch ends existing sessions.</param>
public sealed record AuthUser(
    string Id,
    string UserName,
    string Email,
    string DisplayName,
    string PasswordHash,
    string SecurityStamp,
    bool IsActive,
    int FailedLoginCount,
    DateTimeOffset? LockoutEndUtc,
    IReadOnlyList<string> Roles,
    AccountType AccountType);
