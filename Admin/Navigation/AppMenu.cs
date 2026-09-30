using System.Security.Claims;
using Admin.Auth;
using MudBlazor;
using R = Admin.Auth.QpsRoles;

namespace Admin.Navigation;

/// <summary>A menu link (Href set) or a group (Children set).</summary>
public sealed record MenuNode(string Title, string? Href = null, string? Icon = null, IReadOnlyList<MenuNode>? Children = null)
{
    public bool IsGroup => Children is { Count: > 0 };
}

/// <summary>
/// The left menu, built with the same rules as the old NavMenu.razor: role IDs (QpsRoles) and
/// the User_Management / Menu modules decide which groups and links a user gets. Groups with the
/// same title are merged and duplicate links dropped, so a user with several roles sees each link once.
/// Hiding a link is only convenience: the page itself must still check access.
/// </summary>
public sealed class AppMenu
{
    private static MenuNode L(string title, string href) => new(title, href);
    private static MenuNode G(string title, params MenuNode[] children) => new(title, Children: children);

    public IReadOnlyList<MenuNode> For(ClaimsPrincipal user)
    {
        bool Is(string role) => R.Has(user, role);
        var isAdministrator = Is(R.Administrator);
        var isBuyer = Is(R.Buyer);
        var isQC = Is(R.QC);
        var isView = Is(R.View);
        var isQA = Is(R.QA);
        var isQAM = Is(R.QAM);
        var isQAAdmin = Is(R.QAAdmin);
        var isCH = Is(R.CategoryHead);
        var isACH = Is(R.AssociateCategoryHead);
        var isMerchandiser = Is(R.Merchandiser);
        var isASN = Is(R.ASN);
        var isVendor = R.IsVendor(user);

        var groups = new List<(string Title, string Icon, List<MenuNode> Items)>();
        void Add(string title, string icon, params MenuNode[] items)
        {
            var g = groups.FirstOrDefault(x => x.Title == title);
            if (g.Items is null)
            {
                g = (title, icon, new List<MenuNode>());
                groups.Add(g);
            }
            foreach (var item in items)
                if (item.IsGroup || !g.Items.Any(x => !x.IsGroup && string.Equals(x.Href, item.Href, StringComparison.OrdinalIgnoreCase)))
                    g.Items.Add(item);
        }

        MenuNode[] submitterLinks =
        [
            L("All", "/AllRecPenOngInspections"), L("Submitted", "/InspectionList"), L("Pending", "/PendingInspectionListNew"),
            L("Report", "/InspectionsReportNew"), L("Cancelled", "/CanceledInspections"), L("ASN Generated", "/AsnGenList"),
        ];

        // ---- Inspection Requests ----
        if (isQAM || isQAAdmin || isMerchandiser || isQA || isVendor || isACH || isCH || isBuyer)
        {
            var items = new List<MenuNode>();
            if (isQAM || isQAAdmin || isMerchandiser)
                items.AddRange([
                    L("All", "/AllRecPenOngInspections"), L("Received", "/InspectionList"), L("Ongoing", "/OngoingInspections"),
                    L("Pending", "/PendingInspectionListNew"), L("Report", "/InspectionsReportNew"), L("Cancelled", "/CanceledInspections"),
                    G("ASN", L("ASN Generated", "/AsnGenList")),
                ]);
            if (isQAAdmin)
                items.Add(L("Bulk Download", "/bulk-download"));
            if (isQA)
                items.AddRange([
                    L("Ongoing", "/OngoingInspections"), L("Pending", "/PendingInspectionListNew"),
                    L("Report", "/InspectionsReportNew"), L("Failed", "/FailedInspections"),
                ]);
            if (isVendor)
                items.AddRange(submitterLinks);
            if (isCH || isACH || isBuyer)   // old NavMenu had "isACH || isACH"; Category Head (1010) is included on purpose
                items.AddRange(submitterLinks);
            Add("Inspection Requests", Icons.Material.Outlined.FactCheck, [.. items]);
        }

        if (isASN)
            Add("ASN", Icons.Material.Outlined.LocalShipping, L("ASN Request", "/AsnReqList"), L("ASN Generated", "/AsnGenList"));

        if (isQAAdmin)
            Add("Audit Requests", Icons.Material.Outlined.Assignment, L("New Vendor(s)", "/NewAuditRequests"), L("Old Vendor(s)", "/OldAuditRequests"));

        if (isQAAdmin || isAdministrator || isBuyer || isQC || isView || isMerchandiser)
            Add("PPS", Icons.Material.Outlined.Checkroom, L("PPS Requests", "/pps-list"));

        if (isQAM || isQAAdmin)
            Add("Vendor Management", Icons.Material.Outlined.Storefront, L("Vendor Onboard", "/VendorOnBoarding"), L("Reset Password", "/resetpassword"));

        if (isQAAdmin)
            Add("Master Data", Icons.Material.Outlined.Tune,
                L("Email Management", "/emailList"), L("Color Codes", "/colorCodeList"), L("QA Location", "/QAMaster"), L("Hierarchy", "/HierarchyList"));

        if (R.HasModule(user, R.ModuleUserManagement))
        {
            Add("Settings", Icons.Material.Outlined.Settings,
                L("User Management", "/User-Master"), L("User Roles", "/Role-Master"), L("Vendor Management", "/Vendor-Master"), L("Reset Password", "/resetpassword"));
            Add("Audit Requests", Icons.Material.Outlined.Assignment,
                L("New Vendor(s)", "/NewAuditRequests"), L("Old Vendor(s)", "/OldAuditRequests"), L("Audit Wizard", "/audit/new"), L("Bulk Download", "/bulk-download"));
        }

        // The old NavMenu also listed the dynamic items from GET_MENUS_LIST under this group
        // (RecursiveAdminNavMenu, with encrypted Page_Id links). Add them here when that's moved over.
        if (R.HasModule(user, R.ModuleMenu))
            Add("Menu", Icons.Material.Outlined.List, L("Edit", "/Menu-Master"));

        return groups.Select(g => new MenuNode(g.Title, Icon: g.Icon, Children: g.Items)).ToList();
    }
}
