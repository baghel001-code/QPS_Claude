using System.Collections.Concurrent;

namespace Admin.Auth;

/// <summary>
/// DEVELOPMENT ONLY. Loses everything on restart and does not work across servers.
/// Seeds employees admin (Admin role) and qa1 (QA role), and vendor vendor1 (must change password),
/// all with password ChangeMe!2026, to try the landing pages.
/// </summary>
public sealed class InMemoryAuthUserStore : IAuthUserStore
{
    private readonly ConcurrentDictionary<string, AuthUser> _users = new();
    private readonly ConcurrentDictionary<string, (string UserId, DateTimeOffset ExpiresUtc)> _resetTokens = new();
    private readonly TimeProvider _clock;

    public InMemoryAuthUserStore(AppPasswordHasher hasher, TimeProvider clock)
    {
        _clock = clock;
        AuthUser[] seed =
        [
            new("E:1", "admin", "admin@example.com", "Administrator", "", NewStamp(),
                IsActive: true, FailedLoginCount: 0, LockoutEndUtc: null, Roles: ["Admin"], AccountType.Employee),
            new("E:2", "qa1", "qa1@example.com", "QA Inspector", "", NewStamp(),
                IsActive: true, FailedLoginCount: 0, LockoutEndUtc: null, Roles: ["QA"], AccountType.Employee),
            new("V:1", "vendor1", "vendor1@example.com", "Sample Vendor Pvt Ltd", "", NewStamp(),
                IsActive: true, FailedLoginCount: 0, LockoutEndUtc: null, Roles: ["Vendor"], AccountType.Vendor)
            { MustChangePassword = true },
        ];
        foreach (var user in seed)
            _users[user.Id] = user with { PasswordHash = hasher.HashPassword("ChangeMe!2026") };
    }

    public Task<AuthUser?> FindByLoginAsync(string login, AccountType type, CancellationToken ct = default) =>
        Task.FromResult(_users.Values.FirstOrDefault(u => u.AccountType == type &&
            (string.Equals(u.UserName, login, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(u.Email, login, StringComparison.OrdinalIgnoreCase))));

    public Task<AuthUser?> FindByIdAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult(_users.GetValueOrDefault(userId));

    public Task<int> RecordFailedLoginAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult(Update(userId, u => u with { FailedLoginCount = u.FailedLoginCount + 1 })?.FailedLoginCount ?? 0);

    public Task LockOutAsync(string userId, DateTimeOffset untilUtc, CancellationToken ct = default)
    {
        Update(userId, u => u with { FailedLoginCount = 0, LockoutEndUtc = untilUtc });
        return Task.CompletedTask;
    }

    public Task ClearLockoutAsync(string userId, CancellationToken ct = default)
    {
        Update(userId, u => u with { FailedLoginCount = 0, LockoutEndUtc = null });
        return Task.CompletedTask;
    }

    public Task SetPasswordHashAsync(string userId, string passwordHash, bool rotateSecurityStamp, CancellationToken ct = default)
    {
        Update(userId, u => u with
        {
            PasswordHash = passwordHash,
            SecurityStamp = rotateSecurityStamp ? NewStamp() : u.SecurityStamp,
        });
        return Task.CompletedTask;
    }

    public Task SavePasswordResetTokenAsync(string userId, string tokenHash, DateTimeOffset expiresUtc, CancellationToken ct = default)
    {
        foreach (var (hash, entry) in _resetTokens)
            if (entry.UserId == userId) _resetTokens.TryRemove(hash, out _);
        _resetTokens[tokenHash] = (userId, expiresUtc);
        return Task.CompletedTask;
    }

    public Task<string?> FindUserIdByPasswordResetTokenAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(_resetTokens.TryGetValue(tokenHash, out var e) && e.ExpiresUtc > _clock.GetUtcNow() ? e.UserId : null);

    public Task<string?> ConsumePasswordResetTokenAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(_resetTokens.TryRemove(tokenHash, out var e) && e.ExpiresUtc > _clock.GetUtcNow() ? e.UserId : null);

    private AuthUser? Update(string userId, Func<AuthUser, AuthUser> change)
    {
        while (_users.TryGetValue(userId, out var current))
        {
            var updated = change(current);
            if (_users.TryUpdate(userId, updated, current)) return updated;
        }
        return null;
    }

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}
