using Microsoft.AspNetCore.Authorization;

namespace Admin.Auth.Access;

/// <summary>The five actions a permission can allow on a resource (page).</summary>
public enum PermAction { View, Create, Edit, Approve, Export }

/// <summary>
/// A protected page or feature and the actions it supports. Defined in code (AccessCatalog),
/// copied to the database at startup; which roles get which action is data (RolePermission).
/// </summary>
/// <param name="Code">Stable id, e.g. "Inspection.Ongoing". Never rename: grants refer to it.</param>
public sealed record AccessResource(string Code, string Module, string Name, string? Href, PermAction[] Actions)
{
    public string PermissionFor(PermAction action) => Perm.Code(Code, action);
}

/// <summary>Permission codes ("Inspection.Ongoing.Edit") and their policy names ("perm:Inspection.Ongoing.Edit").</summary>
public static class Perm
{
    public const string PolicyPrefix = "perm:";

    public static string Code(string resource, PermAction action) => $"{resource}.{action}";

    /// <summary>Policy name for AuthorizeView / IAuthorizationService, e.g. Perm.Policy(Res.PpsRequests, PermAction.Approve).</summary>
    public static string Policy(string resource, PermAction action) => PolicyPrefix + Code(resource, action);
}

/// <summary>
/// Page attribute: <c>@attribute [RequirePermission(Res.InspectionOngoing, PermAction.View)]</c>.
/// Same as [Authorize(Policy = "perm:Inspection.Ongoing.View")], without typing the string.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string resource, PermAction action = PermAction.View)
        : base(Perm.Policy(resource, action)) { }
}

/// <summary>Resource codes, for use in attributes (they must be constants).</summary>
public static class Res
{
    public const string InspectionAll = "Inspection.All";
    public const string InspectionRequests = "Inspection.Requests";
    public const string InspectionOngoing = "Inspection.Ongoing";
    public const string InspectionPending = "Inspection.Pending";
    public const string InspectionReport = "Inspection.Report";
    public const string InspectionCancelled = "Inspection.Cancelled";
    public const string InspectionFailed = "Inspection.Failed";
    public const string InspectionBulkDownload = "Inspection.BulkDownload";

    public const string AsnRequest = "Asn.Request";
    public const string AsnGenerated = "Asn.Generated";

    public const string AuditNewVendors = "Audit.NewVendors";
    public const string AuditOldVendors = "Audit.OldVendors";
    public const string AuditWizard = "Audit.Wizard";

    public const string PpsRequests = "Pps.Requests";

    public const string VendorOnboard = "Vendor.Onboard";
    public const string VendorResetPassword = "Vendor.ResetPassword";

    public const string MasterEmail = "Master.Email";
    public const string MasterColorCodes = "Master.ColorCodes";
    public const string MasterQaLocation = "Master.QaLocation";
    public const string MasterHierarchy = "Master.Hierarchy";

    public const string SettingsUsers = "Settings.Users";
    public const string SettingsRoles = "Settings.Roles";       // roles and the role-permission matrix
    public const string SettingsVendors = "Settings.Vendors";
    public const string SettingsMenu = "Settings.Menu";
}
