using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Admin.Auth;

/// <summary>
/// Authorization policies for the dashboards and menus. Use on pages as
/// <c>@attribute [Authorize(Policy = AppPolicies.QA)]</c>. Role names are compared
/// case-insensitively (the built-in [Authorize(Roles = ...)] is case-sensitive).
/// </summary>
public static class AppPolicies
{
    public const string Employee = "qps.employee";
    public const string Vendor = "qps.vendor";
    public const string Admin = "qps.role.admin";
    public const string Buyer = "qps.role.buyer";
    public const string QA = "qps.role.qa";
    public const string QAM = "qps.role.qam";

    public static void Register(AuthorizationOptions options)
    {
        options.AddPolicy(Employee, p => p.RequireAuthenticatedUser().RequireAssertion(c => IsType(c.User, AccountType.Employee)));
        options.AddPolicy(Vendor, p => p.RequireAuthenticatedUser().RequireAssertion(c => IsType(c.User, AccountType.Vendor)));
        AddRolePolicy(options, Admin, "Admin");
        AddRolePolicy(options, Buyer, "Buyer");
        AddRolePolicy(options, QA, "QA");
        AddRolePolicy(options, QAM, "QAM");
    }

    public static bool IsType(ClaimsPrincipal user, AccountType type) =>
        user.FindFirst(AuthClaimTypes.AccountType)?.Value == type.ToString();

    public static bool HasRole(ClaimsPrincipal user, string role) =>
        user.FindAll(ClaimTypes.Role).Any(c => string.Equals(c.Value, role, StringComparison.OrdinalIgnoreCase));

    // Dashboard roles are employee roles: a vendor account never passes, whatever roles it carries.
    private static void AddRolePolicy(AuthorizationOptions options, string name, string role) =>
        options.AddPolicy(name, p => p.RequireAuthenticatedUser()
            .RequireAssertion(c => IsType(c.User, AccountType.Employee) && HasRole(c.User, role)));
}
