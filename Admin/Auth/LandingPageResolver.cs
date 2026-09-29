using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Admin.Auth;

/// <summary>
/// Decides where a signed-in user lands, from the claims set at sign-in:
/// <list type="number">
/// <item>must change password → ChangePassword page</item>
/// <item>vendor → Vendor dashboard</item>
/// <item>employee → the first matching entry in EmployeeRoles, else EmployeeDefault</item>
/// </list>
/// Configured in appsettings.json under "Auth:LandingPages".
/// </summary>
public sealed class LandingPageResolver(IOptions<AuthSettings> options, ILogger<LandingPageResolver> logger)
{
    // Used when appsettings.json has no EmployeeRoles list. Most privileged first.
    private static readonly RoleLandingPage[] DefaultEmployeeRoles =
    [
        new() { Role = "Admin", Path = "/AdminDashboard" },
        new() { Role = "QAM", Path = "/QAMDashboard" },
        new() { Role = "QA", Path = "/QADashboard" },
        new() { Role = "Buyer", Path = "/BuyerDashboard" },
    ];

    /// <summary>The user's own dashboard.</summary>
    public string For(ClaimsPrincipal user)
    {
        var pages = options.Value.LandingPages;

        if (user.FindFirst(AuthClaimTypes.MustChangePassword)?.Value == "true")
            return Checked(pages.ChangePassword, "ChangePassword");

        if (user.FindFirst(AuthClaimTypes.AccountType)?.Value == nameof(AccountType.Vendor))
            return Checked(pages.Vendor, "Vendor");

        var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rules = pages.EmployeeRoles.Count > 0 ? pages.EmployeeRoles : (IReadOnlyList<RoleLandingPage>)DefaultEmployeeRoles;
        foreach (var rule in rules)
            if (roles.Contains(rule.Role))
                return Checked(rule.Path, $"EmployeeRoles[{rule.Role}]");

        return Checked(pages.EmployeeDefault, "EmployeeDefault");
    }

    /// <summary>
    /// After sign-in: a user who must change their password always goes there first.
    /// Otherwise a ReturnUrl from a protected page wins (the user asked for that page),
    /// and without one ("/") they go to their dashboard.
    /// </summary>
    public string AfterSignIn(ClaimsPrincipal user, string safeReturnUrl)
    {
        if (user.FindFirst(AuthClaimTypes.MustChangePassword)?.Value == "true")
            return For(user);
        return safeReturnUrl == "/" ? For(user) : safeReturnUrl;
    }

    // A typo in appsettings must not turn into an open redirect or a crash.
    private string Checked(string path, string setting)
    {
        if (!string.IsNullOrWhiteSpace(path) && UrlSafety.IsLocal(path))
            return path;
        logger.LogError("Auth:LandingPages:{Setting} is not a local path ('{Path}'); using /", setting, path);
        return "/";
    }
}
