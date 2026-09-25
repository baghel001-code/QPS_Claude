namespace Admin.Auth;

/// <summary>What the auth module needs to know about a user. Map it from your own user table.</summary>
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
    IReadOnlyList<string> Roles);
