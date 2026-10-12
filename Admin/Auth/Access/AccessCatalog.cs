using A = Admin.Auth.Access.PermAction;
using I = MudBlazor.Icons.Material.Outlined;
using R = Admin.Auth.QpsRoles;

namespace Admin.Auth.Access;

/// <summary>
/// Every protected page and the actions it supports, plus the default access (role grants and
/// menu) written to an empty database. The defaults reproduce the old fixed NavMenu, so users see
/// the same menu on day one; after that, access is managed in the database (User Management).
/// To protect a new page: add it here and to <see cref="Res"/>, then put
/// [RequirePermission(Res.X, PermAction.View)] on the page.
/// </summary>
public static class AccessCatalog
{
    /// <summary>Module flags (the old MenuService.GetModules()) act as roles with these keys.</summary>
    public const string ModuleUserManagement = "module:" + R.ModuleUserManagement;
    public const string ModuleMenu = "module:" + R.ModuleMenu;

    private static readonly A[] ViewExport = [A.View, A.Export];
    private static readonly A[] Crud = [A.View, A.Create, A.Edit];
    private static readonly A[] All5 = [A.View, A.Create, A.Edit, A.Approve, A.Export];

    public static readonly IReadOnlyList<AccessResource> Resources =
    [
        new(Res.InspectionAll, "Inspection Requests", "All requests", "/AllRecPenOngInspections", ViewExport),
        new(Res.InspectionRequests, "Inspection Requests", "Received / submitted requests", "/InspectionList", [A.View, A.Create, A.Approve, A.Export]),
        new(Res.InspectionOngoing, "Inspection Requests", "Ongoing inspections", "/OngoingInspections", [A.View, A.Edit, A.Export]),
        new(Res.InspectionPending, "Inspection Requests", "Pending inspections", "/PendingInspectionListNew", [A.View, A.Edit, A.Export]),
        new(Res.InspectionReport, "Inspection Requests", "Inspection reports", "/InspectionsReportNew", ViewExport),
        new(Res.InspectionCancelled, "Inspection Requests", "Cancelled requests", "/CanceledInspections", ViewExport),
        new(Res.InspectionFailed, "Inspection Requests", "Failed inspections", "/FailedInspections", ViewExport),
        new(Res.InspectionBulkDownload, "Inspection Requests", "Bulk download", "/bulk-download", [A.Export]),

        new(Res.AsnRequest, "ASN", "ASN requests", "/AsnReqList", Crud),
        new(Res.AsnGenerated, "ASN", "ASN generated", "/AsnGenList", ViewExport),

        new(Res.AuditNewVendors, "Audit Requests", "New vendor audits", "/NewAuditRequests", [A.View, A.Approve, A.Export]),
        new(Res.AuditOldVendors, "Audit Requests", "Existing vendor audits", "/OldAuditRequests", [A.View, A.Approve, A.Export]),
        new(Res.AuditWizard, "Audit Requests", "Audit wizard", "/audit/new", [A.View, A.Create]),

        new(Res.PpsRequests, "PPS", "PPS requests", "/pps-list", All5),

        new(Res.VendorOnboard, "Vendor Management", "Vendor onboarding", "/VendorOnBoarding", [A.View, A.Create, A.Edit, A.Approve]),
        new(Res.VendorResetPassword, "Vendor Management", "Reset vendor password", "/resetpassword", [A.View, A.Edit]),

        new(Res.MasterEmail, "Master Data", "Email management", "/emailList", Crud),
        new(Res.MasterColorCodes, "Master Data", "Color codes", "/colorCodeList", Crud),
        new(Res.MasterQaLocation, "Master Data", "QA locations", "/QAMaster", Crud),
        new(Res.MasterHierarchy, "Master Data", "Hierarchy", "/HierarchyList", Crud),

        new(Res.SettingsUsers, "Settings", "Users", "/User-Master", [A.View, A.Create, A.Edit, A.Export]),
        new(Res.SettingsRoles, "Settings", "Roles and permissions", "/Role-Master", Crud),
        new(Res.SettingsVendors, "Settings", "Vendors", "/Vendor-Master", Crud),
        new(Res.SettingsMenu, "Settings", "Menu", "/Menu-Master", Crud),
    ];

    /// <summary>All permission codes that exist ("Inspection.Ongoing.View", …).</summary>
    public static readonly IReadOnlySet<string> AllPermissions =
        Resources.SelectMany(r => r.Actions.Select(r.PermissionFor)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static AccessSeed Seed { get; } = new(BuildGrants(), BuildMenu());

    private static List<RoleGrant> BuildGrants()
    {
        var grants = new List<RoleGrant>();
        void Grant(string[] roles, string resource, params A[] actions)
        {
            foreach (var role in roles)
                foreach (var action in actions)
                    grants.Add(new RoleGrant(role, Perm.Code(resource, action)));
        }

        // Same groups as the old NavMenu.
        string[] inspectionManagers = [R.QAM, R.QAAdmin, R.Merchandiser];
        string[] submitters = [R.Vendor, R.CategoryHead, R.AssociateCategoryHead, R.Buyer];

        Grant(inspectionManagers, Res.InspectionAll, A.View);
        Grant(inspectionManagers, Res.InspectionRequests, A.View, A.Approve);
        Grant(inspectionManagers, Res.InspectionOngoing, A.View);
        Grant(inspectionManagers, Res.InspectionPending, A.View);
        Grant(inspectionManagers, Res.InspectionReport, A.View, A.Export);
        Grant(inspectionManagers, Res.InspectionCancelled, A.View);
        Grant(inspectionManagers, Res.AsnGenerated, A.View);

        Grant([R.QA], Res.InspectionOngoing, A.View, A.Edit);
        Grant([R.QA], Res.InspectionPending, A.View, A.Edit);
        Grant([R.QA], Res.InspectionReport, A.View);
        Grant([R.QA], Res.InspectionFailed, A.View);

        Grant(submitters, Res.InspectionAll, A.View);
        Grant(submitters, Res.InspectionRequests, A.View, A.Create);
        Grant(submitters, Res.InspectionPending, A.View);
        Grant(submitters, Res.InspectionReport, A.View);
        Grant(submitters, Res.InspectionCancelled, A.View);
        Grant(submitters, Res.AsnGenerated, A.View);

        Grant([R.ASN], Res.AsnRequest, A.View, A.Create, A.Edit);
        Grant([R.ASN], Res.AsnGenerated, A.View);

        Grant([R.QAAdmin], Res.InspectionBulkDownload, A.Export);
        Grant([R.QAAdmin], Res.AuditNewVendors, A.View, A.Approve);
        Grant([R.QAAdmin], Res.AuditOldVendors, A.View, A.Approve);
        Grant([R.QAAdmin], Res.PpsRequests, A.View, A.Approve);
        Grant([R.QAAdmin, R.QAM], Res.VendorOnboard, A.View, A.Create, A.Edit);
        Grant([R.QAAdmin], Res.VendorOnboard, A.Approve);
        Grant([R.QAAdmin, R.QAM], Res.VendorResetPassword, A.View, A.Edit);
        foreach (var master in new[] { Res.MasterEmail, Res.MasterColorCodes, Res.MasterQaLocation, Res.MasterHierarchy })
            Grant([R.QAAdmin], master, A.View, A.Create, A.Edit);

        Grant([R.Administrator, R.Buyer, R.QC, R.View, R.Merchandiser], Res.PpsRequests, A.View);

        // Module flags.
        foreach (var settings in new[] { Res.SettingsUsers, Res.SettingsRoles, Res.SettingsVendors })
            Grant([ModuleUserManagement], settings, A.View, A.Create, A.Edit);
        Grant([ModuleUserManagement], Res.SettingsUsers, A.Export);
        Grant([ModuleUserManagement], Res.VendorResetPassword, A.View, A.Edit);
        Grant([ModuleUserManagement], Res.AuditNewVendors, A.View);
        Grant([ModuleUserManagement], Res.AuditOldVendors, A.View);
        Grant([ModuleUserManagement], Res.AuditWizard, A.View, A.Create);
        Grant([ModuleUserManagement], Res.InspectionBulkDownload, A.Export);
        Grant([ModuleMenu], Res.SettingsMenu, A.View, A.Create, A.Edit);

        return grants.Distinct().ToList();
    }

    private static List<MenuItem> BuildMenu()
    {
        var menu = new List<MenuItem>();
        var id = 0;
        int Group(string title, string icon, string? permission = null)
        {
            menu.Add(new MenuItem(++id, null, title, null, icon, id * 10, permission));
            return id;
        }
        void Link(int parent, string title, string href, string permission) =>
            menu.Add(new MenuItem(++id, parent, title, href, null, id * 10, permission));
        static string V(string res) => Perm.Code(res, A.View);

        var inspection = Group("Inspection Requests", I.FactCheck);
        Link(inspection, "All", "/AllRecPenOngInspections", V(Res.InspectionAll));
        // Same page, two titles: "Received" for those who approve, "Submitted" for those who submit.
        // A user with both sees it once (the first one wins).
        Link(inspection, "Received", "/InspectionList", Perm.Code(Res.InspectionRequests, A.Approve));
        Link(inspection, "Submitted", "/InspectionList", Perm.Code(Res.InspectionRequests, A.Create));
        Link(inspection, "Ongoing", "/OngoingInspections", V(Res.InspectionOngoing));
        Link(inspection, "Pending", "/PendingInspectionListNew", V(Res.InspectionPending));
        Link(inspection, "Report", "/InspectionsReportNew", V(Res.InspectionReport));
        Link(inspection, "Failed", "/FailedInspections", V(Res.InspectionFailed));
        Link(inspection, "Cancelled", "/CanceledInspections", V(Res.InspectionCancelled));
        Link(inspection, "ASN Generated", "/AsnGenList", $"{V(Res.AsnGenerated)},{V(Res.InspectionAll)}");
        Link(inspection, "Bulk Download", "/bulk-download", Perm.Code(Res.InspectionBulkDownload, A.Export));

        var asn = Group("ASN", I.LocalShipping, V(Res.AsnRequest));
        Link(asn, "ASN Request", "/AsnReqList", V(Res.AsnRequest));
        Link(asn, "ASN Generated", "/AsnGenList", V(Res.AsnGenerated));

        var audit = Group("Audit Requests", I.Assignment);
        Link(audit, "New Vendor(s)", "/NewAuditRequests", V(Res.AuditNewVendors));
        Link(audit, "Old Vendor(s)", "/OldAuditRequests", V(Res.AuditOldVendors));
        Link(audit, "Audit Wizard", "/audit/new", V(Res.AuditWizard));
        Link(audit, "Bulk Download", "/bulk-download", $"{Perm.Code(Res.InspectionBulkDownload, A.Export)},{V(Res.AuditWizard)}");

        var pps = Group("PPS", I.Checkroom);
        Link(pps, "PPS Requests", "/pps-list", V(Res.PpsRequests));

        var vendors = Group("Vendor Management", I.Storefront, V(Res.VendorOnboard));
        Link(vendors, "Vendor Onboard", "/VendorOnBoarding", V(Res.VendorOnboard));
        Link(vendors, "Reset Password", "/resetpassword", V(Res.VendorResetPassword));

        var master = Group("Master Data", I.Tune);
        Link(master, "Email Management", "/emailList", V(Res.MasterEmail));
        Link(master, "Color Codes", "/colorCodeList", V(Res.MasterColorCodes));
        Link(master, "QA Location", "/QAMaster", V(Res.MasterQaLocation));
        Link(master, "Hierarchy", "/HierarchyList", V(Res.MasterHierarchy));

        var settings = Group("Settings", I.Settings, V(Res.SettingsUsers));
        Link(settings, "User Management", "/User-Master", V(Res.SettingsUsers));
        Link(settings, "User Roles", "/Role-Master", V(Res.SettingsRoles));
        Link(settings, "Vendor Management", "/Vendor-Master", V(Res.SettingsVendors));
        Link(settings, "Reset Password", "/resetpassword", V(Res.VendorResetPassword));

        var menuGroup = Group("Menu", I.List);
        Link(menuGroup, "Edit", "/Menu-Master", V(Res.SettingsMenu));

        return menu;
    }
}
