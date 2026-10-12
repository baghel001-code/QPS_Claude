namespace Admin.Auth;

/// <summary>
/// One row of the sign-in query (USER_MASTER joined with auth.UserSecurity), as read from the
/// database. Property names match the column names ignoring case, so Dapper maps them directly;
/// the two underscore columns are aliased in the query (REGION_IDS AS RegionIds, GST_NO AS GstNo).
/// Convert it with <see cref="ToAuthUser"/> before returning it from IAuthUserStore.
/// </summary>
public sealed class UserLoginRecord
{
    public long Id { get; set; }
    public string UserName { get; set; } = "";
    public string? MobileNumber { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }

    /// <summary>Vendors only; empty for employees (their password is checked by the employee API).</summary>
    public string? PasswordHash { get; set; }
    public string? SecurityStamp { get; set; }
    public bool IsActive { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }

    /// <summary>One role ID ("1006") or several separated by commas ("1007,1008").</summary>
    public string? RoleId { get; set; }

    /// <summary>Comma-separated region IDs.</summary>
    public string? RegionIds { get; set; }

    /// <summary>Vendors only.</summary>
    public string? GstNo { get; set; }
    public bool MustChangePassword { get; set; }

    /// <summary>"Employee" or "Vendor".</summary>
    public string AccountType { get; set; } = "";

    public AuthUser ToAuthUser(IReadOnlyList<string>? modules = null)
    {
        var type = Enum.TryParse<AccountType>(AccountType?.Trim(), ignoreCase: true, out var t)
            ? t
            : throw new InvalidOperationException($"User {Id} has an unknown ACCOUNTTYPE '{AccountType}'.");

        // Vendors get the "Vendor" role (menus and dashboard); employees their role IDs.
        IReadOnlyList<string> roles = type == Auth.AccountType.Vendor
            ? [QpsRoles.Vendor]
            : SplitIds(RoleId);

        return new AuthUser(
            // Prefixed so an employee and a vendor with the same ID can't be mixed up.
            Id: (type == Auth.AccountType.Vendor ? "V:" : "E:") + Id,
            UserName: UserName.Trim(),
            Email: Email?.Trim() ?? "",
            DisplayName: string.IsNullOrWhiteSpace(DisplayName) ? UserName.Trim() : DisplayName.Trim(),
            PasswordHash: PasswordHash ?? "",
            // No auth.UserSecurity row → no stamp → the session check fails; insert the row first.
            SecurityStamp: SecurityStamp ?? "",
            IsActive: IsActive,
            FailedLoginCount: FailedLoginCount,
            LockoutEndUtc: LockoutEndUtc,
            Roles: roles,
            AccountType: type)
        {
            MustChangePassword = MustChangePassword,
            Modules = modules ?? [],
        };
    }

    /// <summary>Reverses the "E:"/"V:" prefix for FindByIdAsync.</summary>
    public static bool TryParseId(string authUserId, out AccountType type, out long id)
    {
        type = authUserId.StartsWith("V:", StringComparison.Ordinal) ? Auth.AccountType.Vendor : Auth.AccountType.Employee;
        id = 0;
        return authUserId.Length > 2 && authUserId[1] == ':' && authUserId[0] is ('E' or 'V')
               && long.TryParse(authUserId.AsSpan(2), out id);
    }

    public static IReadOnlyList<string> SplitIds(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
