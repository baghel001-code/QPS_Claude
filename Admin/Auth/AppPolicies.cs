using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Admin.Auth;

/// <summary>
/// Authorization policies. Use on pages as <c>@attribute [Authorize(Policy = AppPolicies.Vendor)]</c>.
/// For role checks use <see cref="QpsRoles.Has"/> (role IDs) or add a policy here.
/// </summary>
public static class AppPolicies
{
    public const string Employee = "qps.employee";
    public const string Vendor = "qps.vendor";

    public static void Register(AuthorizationOptions options)
    {
        options.AddPolicy(Employee, p => p.RequireAuthenticatedUser().RequireAssertion(c => IsType(c.User, AccountType.Employee)));
        options.AddPolicy(Vendor, p => p.RequireAuthenticatedUser().RequireAssertion(c => IsType(c.User, AccountType.Vendor)));
    }

    public static bool IsType(ClaimsPrincipal user, AccountType type) =>
        user.FindFirst(AuthClaimTypes.AccountType)?.Value == type.ToString();
}
