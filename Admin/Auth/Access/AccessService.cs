using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Admin.Auth.Access;

/// <summary>What one signed-in user may do: effective permissions and ABAC scopes.</summary>
public sealed class UserAccess(IReadOnlySet<string> permissions, IReadOnlyList<UserScope> scopes)
{
    public static readonly UserAccess None = new(new HashSet<string>(), []);

    public IReadOnlySet<string> Permissions { get; } = permissions;
    public IReadOnlyList<UserScope> Scopes { get; } = scopes;

    public bool Has(string permission) => Permissions.Contains(permission);

    public bool Has(string resource, PermAction action) => Has(Perm.Code(resource, action));

    /// <summary>True for null/empty; otherwise every comma-separated code must be granted.</summary>
    public bool HasAll(string? permissions) =>
        string.IsNullOrWhiteSpace(permissions) ||
        permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(Has);

    /// <summary>The values of one scope type, e.g. the user's region IDs. Empty = no data of that kind.</summary>
    public IReadOnlyList<string> Scope(string scopeType) =>
        Scopes.Where(s => string.Equals(s.ScopeType, scopeType, StringComparison.OrdinalIgnoreCase)).Select(s => s.ScopeValue).ToList();

    public bool InScope(string scopeType, string? value) =>
        value is not null && Scope(scopeType).Contains(value, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Effective access per user: permissions of all their roles (+ module flags), plus user grants,
/// minus user denies (deny wins). Cached for 5 minutes; call <see cref="InvalidateUser"/> or
/// <see cref="InvalidateAll"/> after changing access so it applies at once.
/// Singleton: uses its own DI scope for the store, so a scoped DbContext is never shared.
/// </summary>
public sealed class AccessService(IServiceScopeFactory scopeFactory, IMemoryCache cache, ILogger<AccessService> logger)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, int> _userVersions = new();
    private int _globalVersion;

    public async Task<UserAccess> GetAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Identity?.IsAuthenticated != true || userId is null)
            return UserAccess.None;

        var roleKeys = RoleKeys(user);
        var key = $"qps.access:{userId}:{string.Join(',', roleKeys)}:{Volatile.Read(ref _globalVersion)}:{_userVersions.GetValueOrDefault(userId)}";
        if (cache.TryGetValue(key, out UserAccess? cached) && cached is not null)
            return cached;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IAccessStore>();

            var permissions = new HashSet<string>(await store.GetRolePermissionsAsync(roleKeys, ct), StringComparer.OrdinalIgnoreCase);
            var exceptions = await store.GetUserPermissionsAsync(userId, ct);
            permissions.UnionWith(exceptions.Where(p => p.Effect == GrantEffect.Grant).Select(p => p.Permission));
            permissions.ExceptWith(exceptions.Where(p => p.Effect == GrantEffect.Deny).Select(p => p.Permission));   // deny wins

            var access = new UserAccess(permissions, await store.GetUserScopesAsync(userId, ct));
            cache.Set(key, access, CacheFor);
            return access;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Not cached: the next request tries again. Until then the user gets no permissions (deny by default).
            logger.LogError(ex, "Could not load access for {UserId}", userId);
            return UserAccess.None;
        }
    }

    public async Task<bool> HasAsync(ClaimsPrincipal user, string resource, PermAction action, CancellationToken ct = default) =>
        (await GetAsync(user, ct)).Has(resource, action);

    public async Task<IReadOnlyList<MenuItem>> GetMenuAsync(CancellationToken ct = default)
    {
        var key = $"qps.menu:{Volatile.Read(ref _globalVersion)}";
        if (cache.TryGetValue(key, out IReadOnlyList<MenuItem>? cached) && cached is not null)
            return cached;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var menu = await scope.ServiceProvider.GetRequiredService<IAccessStore>().GetMenuAsync(ct);
            cache.Set(key, menu, CacheFor);
            return menu;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not load the menu");
            return [];
        }
    }

    /// <summary>After changing one user's roles, exceptions or scopes.</summary>
    public void InvalidateUser(string userId) => _userVersions.AddOrUpdate(userId, 1, (_, v) => v + 1);

    /// <summary>After changing a role's permissions or the menu (affects many users).</summary>
    public void InvalidateAll() => Interlocked.Increment(ref _globalVersion);

    /// <summary>Role IDs from the cookie, plus module flags as "module:User_Management".</summary>
    public static IReadOnlyList<string> RoleKeys(ClaimsPrincipal user) =>
        user.FindAll(ClaimTypes.Role).Select(c => c.Value)
            .Concat(user.FindAll(AuthClaimTypes.Module).Select(c => "module:" + c.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>Requirement behind every "perm:…" policy.</summary>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionHandler(AccessService access, ILogger<PermissionHandler> logger) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!AccessCatalog.AllPermissions.Contains(requirement.Permission))
            logger.LogWarning("Permission {Permission} is not in AccessCatalog (typo?); nobody can be granted it", requirement.Permission);

        if ((await access.GetAsync(context.User)).Has(requirement.Permission))
            context.Succeed(requirement);
    }
}

/// <summary>
/// Turns any policy name "perm:&lt;code&gt;" into a policy at runtime, so permissions need no
/// registration. Other names go to the normal policies (AddAuthorization, fallback policy).
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _policies = new(StringComparer.OrdinalIgnoreCase);

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Perm.PolicyPrefix, StringComparison.OrdinalIgnoreCase))
            return base.GetPolicyAsync(policyName);

        return Task.FromResult<AuthorizationPolicy?>(_policies.GetOrAdd(policyName, name =>
            new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(name[Perm.PolicyPrefix.Length..]))
                .Build()));
    }
}

/// <summary>At startup: copies the catalog (resources, permissions) to the store and seeds an empty database.</summary>
public sealed class AccessCatalogSync(IServiceScopeFactory scopeFactory, IHostEnvironment env, ILogger<AccessCatalogSync> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IAccessStore>();
            if (store is InMemoryAccessStore && !env.IsDevelopment())
                logger.LogWarning("InMemoryAccessStore is in use outside Development: access changes are lost on restart. Register your IAccessStore.");
            await store.SyncAsync(AccessCatalog.Resources, AccessCatalog.Seed, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // The app still starts; pages that need permissions are denied until the store works.
            logger.LogError(ex, "Could not sync the access catalog to the store");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
