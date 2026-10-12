using System.Security.Claims;

namespace Admin.Auth;

/// <summary>
/// QPS role IDs as stored in the role table, their display names, and which role picks the
/// home-page dashboard when a user has several. Roles are ClaimTypes.Role claims holding the ID.
/// </summary>
public static class QpsRoles
{
    public const string Administrator = "1";
    public const string Buyer = "2";
    public const string QC = "3";
    public const string View = "4";
    public const string Admin = "5";
    public const string QA = "1006";
    public const string QAM = "1007";
    public const string QAAdmin = "1008";
    public const string CM = "1009";
    public const string CategoryHead = "1010";
    public const string AssociateCategoryHead = "1011";
    public const string Merchandiser = "1012";
    public const string BFT = "1013";
    public const string ASN = "1014";

    /// <summary>Vendor accounts carry this role (as the old NavMenu checked IsInRole("Vendor")).</summary>
    public const string Vendor = "Vendor";

    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        [Administrator] = "Administrator", [Buyer] = "Buyer", [QC] = "QC", [View] = "View", [Admin] = "Admin",
        [QA] = "QA", [QAM] = "QAM", [QAAdmin] = "QA Admin", [CM] = "CM", [CategoryHead] = "Category Head",
        [AssociateCategoryHead] = "Associate Category Head", [Merchandiser] = "Merchandiser", [BFT] = "BFT", [ASN] = "ASN",
        [Vendor] = "Vendor",
    };

    /// <summary>For users with several roles: the first one they have decides the home dashboard.</summary>
    public static readonly IReadOnlyList<string> DashboardPriority =
    [
        Administrator, Admin, QAAdmin, QAM, QA, CategoryHead, AssociateCategoryHead,
        Merchandiser, Buyer, QC, ASN, BFT, CM, View,
    ];

    // Modules (the old MenuService.GetModules() flags), carried as claims set at sign-in.
    public const string ModuleUserManagement = "User_Management";
    public const string ModuleMenu = "Menu";

    public static bool Has(ClaimsPrincipal user, string roleId) =>
        user.FindAll(ClaimTypes.Role).Any(c => string.Equals(c.Value, roleId, StringComparison.OrdinalIgnoreCase));

    public static bool HasModule(ClaimsPrincipal user, string module) =>
        user.FindAll(AuthClaimTypes.Module).Any(c => string.Equals(c.Value, module, StringComparison.OrdinalIgnoreCase));

    public static bool IsVendor(ClaimsPrincipal user) =>
        AppPolicies.IsType(user, AccountType.Vendor) || Has(user, Vendor);

    /// <summary>"Vendor", the highest-priority role ID the user has, or null for an employee with none.</summary>
    public static string? PrimaryRole(ClaimsPrincipal user) =>
        IsVendor(user) ? Vendor : DashboardPriority.FirstOrDefault(r => Has(user, r));

    /// <summary>Role names for display, e.g. "QAM · QA Admin".</summary>
    public static string Describe(ClaimsPrincipal user)
    {
        if (IsVendor(user))
            return "Vendor";
        var names = user.FindAll(ClaimTypes.Role).Select(c => Names.GetValueOrDefault(c.Value, c.Value)).ToList();
        return names.Count > 0 ? string.Join(" · ", names) : "Employee";
    }
}
