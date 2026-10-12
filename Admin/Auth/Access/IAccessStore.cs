namespace Admin.Auth.Access;

/// <summary>A row of auth.Menu. A group has no Href; a link has one.</summary>
/// <param name="Permission">
/// Needed to see the entry: one code ("Inspection.Ongoing.View"), several separated by commas
/// (all needed), or null (always shown; a group without a permission is shown when any child is).
/// </param>
public sealed record MenuItem(int Id, int? ParentId, string Title, string? Href, string? Icon, int SortOrder, string? Permission, bool IsActive = true);

/// <summary>Role (role ID such as "1007", "Vendor", or "module:User_Management") → permission code.</summary>
public sealed record RoleGrant(string RoleKey, string Permission);

public enum GrantEffect { Grant, Deny }

/// <summary>An exception for one user: an extra permission, or one taken away (Deny always wins).</summary>
public sealed record UserPermission(string UserId, string Permission, GrantEffect Effect);

/// <summary>ABAC values for one user: which regions, categories, DCs or vendors their data is limited to.</summary>
public sealed record UserScope(string UserId, string ScopeType, string ScopeValue);

public static class ScopeTypes
{
    public const string Region = "Region";
    public const string Category = "Category";
    public const string Dc = "DC";
    public const string Vendor = "Vendor";
}

/// <summary>Default access written to an empty database the first time (same as the old fixed menu).</summary>
public sealed record AccessSeed(IReadOnlyList<RoleGrant> RoleGrants, IReadOnlyList<MenuItem> Menu);

/// <summary>
/// Where roles, permissions, menu and scopes live. Implement it against your database (or API)
/// and register it in Program.cs:  builder.Services.AddScoped&lt;IAccessStore, SqlAccessStore&gt;();
/// Tables: docs/sql/access-control.sql. Development default: <see cref="InMemoryAccessStore"/>.
/// User ids are the AuthUser ids ("E:123", "V:88").
/// </summary>
public interface IAccessStore
{
    /// <summary>
    /// At startup: adds resources/permissions that exist in code but not yet in the database
    /// (never deletes), and writes <paramref name="seed"/> when no role grants or menu exist yet.
    /// </summary>
    Task SyncAsync(IReadOnlyList<AccessResource> resources, AccessSeed seed, CancellationToken ct = default);

    /// <summary>Permission codes granted to any of these roles.</summary>
    Task<IReadOnlyList<string>> GetRolePermissionsAsync(IReadOnlyCollection<string> roleKeys, CancellationToken ct = default);

    Task<IReadOnlyList<UserPermission>> GetUserPermissionsAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<UserScope>> GetUserScopesAsync(string userId, CancellationToken ct = default);

    /// <summary>All active menu rows (filtered per user by AccessService).</summary>
    Task<IReadOnlyList<MenuItem>> GetMenuAsync(CancellationToken ct = default);
}
