using System.Security.Claims;
using Admin.Auth;
using MudBlazor;

namespace Admin.Navigation;

/// <summary>One entry in the left menu.</summary>
/// <param name="Section">Menu group heading. Groups appear in the order their first item is listed.</param>
/// <param name="For">Which account type sees it; null = both.</param>
/// <param name="Roles">Employee roles that see it (any match, case-insensitive); null = every user of the type.</param>
/// <param name="Ready">False while the page isn't built: the link opens the "coming soon" page instead.</param>
public sealed record MenuItem(
    string Title, string Href, string Icon, string Section,
    AccountType? For = null, string[]? Roles = null, bool Ready = true);

public sealed record MenuSection(string Title, IReadOnlyList<MenuItem> Items);

/// <summary>
/// The whole QPS menu in one list. Each user sees the items for their account type and roles.
/// Hiding a menu item is only convenience: protect the page itself with [Authorize(Policy = ...)].
/// </summary>
public sealed class AppMenu(LandingPageResolver landing)
{
    private static readonly string[] AllEmployeeRoles = ["Admin", "Buyer", "QA", "QAM"];

    private static readonly MenuItem[] Items =
    [
        // ---- employees ----
        new("Inspection requests", "/InspectionList", Icons.Material.Outlined.FactCheck, "Inspections", AccountType.Employee, ["QA", "QAM", "Buyer", "Admin"]),
        new("Ongoing inspections", "/OngoingInspections", Icons.Material.Outlined.PendingActions, "Inspections", AccountType.Employee, ["QA", "QAM"]),
        new("Assign inspectors", "/AssignInspections", Icons.Material.Outlined.AssignmentInd, "Inspections", AccountType.Employee, ["QAM"], Ready: false),
        new("PPS approvals", "/PPS", Icons.Material.Outlined.Checkroom, "Inspections", AccountType.Employee, ["Buyer", "QAM", "Admin"], Ready: false),
        new("Purchase orders", "/PurchaseOrders", Icons.Material.Outlined.ReceiptLong, "Supply", AccountType.Employee, ["Buyer"], Ready: false),
        new("ASN requests", "/AsnReqList", Icons.Material.Outlined.LocalShipping, "Supply", AccountType.Employee, ["Buyer", "QAM"]),
        new("Vendors", "/VendorManagement", Icons.Material.Outlined.Storefront, "Supply", AccountType.Employee, ["QAM", "Admin"], Ready: false),
        new("Reports", "/Reports", Icons.Material.Outlined.Insights, "Insights", AccountType.Employee, AllEmployeeRoles, Ready: false),
        new("Users & roles", "/Admin/Users", Icons.Material.Outlined.ManageAccounts, "Administration", AccountType.Employee, ["Admin"], Ready: false),
        new("Masters", "/Admin/Masters", Icons.Material.Outlined.Tune, "Administration", AccountType.Employee, ["Admin"], Ready: false),

        // ---- vendors ----
        new("Raise inspection request", "/Vendor/InspectionRequest", Icons.Material.Outlined.AddTask, "Inspections", AccountType.Vendor, Ready: false),
        new("My inspection requests", "/Vendor/Requests", Icons.Material.Outlined.FactCheck, "Inspections", AccountType.Vendor, Ready: false),
        new("ASN", "/Vendor/Asn", Icons.Material.Outlined.LocalShipping, "Shipments", AccountType.Vendor, Ready: false),
        new("Change password", "/changepassword", Icons.Material.Outlined.Password, "Account", AccountType.Vendor),
    ];

    public IReadOnlyList<MenuSection> For(ClaimsPrincipal user)
    {
        var type = AppPolicies.IsType(user, AccountType.Vendor) ? AccountType.Vendor : AccountType.Employee;

        // First entry is always the user's own dashboard (the page they land on after sign-in).
        var visible = new List<MenuItem> { new("Dashboard", landing.For(user), Icons.Material.Outlined.SpaceDashboard, "Overview") };
        visible.AddRange(Items.Where(i =>
            (i.For is null || i.For == type) &&
            (i.Roles is null || i.Roles.Any(r => AppPolicies.HasRole(user, r)))));

        return visible
            .DistinctBy(i => i.Href, StringComparer.OrdinalIgnoreCase)
            .GroupBy(i => i.Section)
            .Select(g => new MenuSection(g.Key, g.ToList()))
            .ToList();
    }

    /// <summary>Where a menu link points: the page itself, or the "coming soon" page while it isn't built.</summary>
    public static string LinkFor(MenuItem item) =>
        item.Ready ? item.Href : $"/coming-soon?page={Uri.EscapeDataString(item.Title)}&path={Uri.EscapeDataString(item.Href)}";
}
