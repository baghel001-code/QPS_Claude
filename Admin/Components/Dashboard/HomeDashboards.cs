using System.Security.Claims;
using Admin.Auth;
using R = Admin.Auth.QpsRoles;

namespace Admin.Components.Dashboard;

/// <summary>
/// Which dashboard the home page ("/") shows, from the user's type and primary role
/// (QpsRoles.PrimaryRole). Every role currently gets a dummy dashboard (RoleDashboard +
/// DashboardCatalog). To give a role a real one, build a component and register it in Custom:
///     [R.QA] = typeof(QaDashboard),
/// It receives no parameters; read the user from the cascading AuthenticationState.
/// </summary>
public static class HomeDashboards
{
    public const string NoRole = "none";

    private static readonly Dictionary<string, Type> Custom = new()
    {
        // [R.QA] = typeof(QaDashboard),
    };

    private static readonly Dictionary<string, DashboardDefinition> Dummy = new()
    {
        [R.Administrator] = DashboardCatalog.Administrator,
        [R.Admin] = DashboardCatalog.Admin,
        [R.QAAdmin] = DashboardCatalog.QAAdmin,
        [R.QAM] = DashboardCatalog.QAM,
        [R.QA] = DashboardCatalog.QA,
        [R.CategoryHead] = DashboardCatalog.CategoryHead,
        [R.AssociateCategoryHead] = DashboardCatalog.AssociateCategoryHead,
        [R.Merchandiser] = DashboardCatalog.Merchandiser,
        [R.Buyer] = DashboardCatalog.Buyer,
        [R.QC] = DashboardCatalog.QC,
        [R.View] = DashboardCatalog.View,
        [R.ASN] = DashboardCatalog.ASN,
        [R.BFT] = DashboardCatalog.BFT,
        [R.CM] = DashboardCatalog.CM,
        [R.Vendor] = DashboardCatalog.Vendor,
        [NoRole] = DashboardCatalog.NoRole,
    };

    public sealed record Choice(string Key, Type Component, IDictionary<string, object>? Parameters);

    public static Choice For(ClaimsPrincipal user)
    {
        var key = R.PrimaryRole(user) ?? NoRole;
        if (Custom.TryGetValue(key, out var type))
            return new(key, type, null);
        var definition = Dummy.GetValueOrDefault(key, DashboardCatalog.NoRole);
        return new(key, typeof(RoleDashboard), new Dictionary<string, object> { [nameof(RoleDashboard.Definition)] = definition });
    }
}
