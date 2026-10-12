using System.Collections.Concurrent;

namespace Admin.Auth.Access;

/// <summary>
/// DEVELOPMENT ONLY. Holds the default access from <see cref="AccessCatalog.Seed"/> in memory;
/// changes are lost on restart. Register your database store to replace it:
///     builder.Services.AddScoped&lt;IAccessStore, SqlAccessStore&gt;();
/// </summary>
public sealed class InMemoryAccessStore : IAccessStore
{
    private readonly ConcurrentDictionary<string, AccessResource> _resources = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RoleGrant> _grants = [];
    private readonly List<MenuItem> _menu = [];
    private readonly List<UserPermission> _userPermissions = [];
    private readonly List<UserScope> _scopes = [];
    private readonly object _lock = new();

    public Task SyncAsync(IReadOnlyList<AccessResource> resources, AccessSeed seed, CancellationToken ct = default)
    {
        foreach (var r in resources)
            _resources.TryAdd(r.Code, r);
        lock (_lock)
        {
            if (_grants.Count == 0) _grants.AddRange(seed.RoleGrants);
            if (_menu.Count == 0) _menu.AddRange(seed.Menu);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetRolePermissionsAsync(IReadOnlyCollection<string> roleKeys, CancellationToken ct = default)
    {
        var keys = roleKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_lock)
            return Task.FromResult<IReadOnlyList<string>>(_grants.Where(g => keys.Contains(g.RoleKey)).Select(g => g.Permission).Distinct().ToList());
    }

    public Task<IReadOnlyList<UserPermission>> GetUserPermissionsAsync(string userId, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<UserPermission>>(_userPermissions.Where(p => p.UserId == userId).ToList());
    }

    public Task<IReadOnlyList<UserScope>> GetUserScopesAsync(string userId, CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<UserScope>>(_scopes.Where(s => s.UserId == userId).ToList());
    }

    public Task<IReadOnlyList<MenuItem>> GetMenuAsync(CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<MenuItem>>(_menu.Where(m => m.IsActive).ToList());
    }
}
