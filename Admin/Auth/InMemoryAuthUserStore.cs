using System.Collections.Concurrent;

namespace Admin.Auth;

/// <summary>
/// DEVELOPMENT ONLY. Loses everything on restart and does not work across servers.
/// Seeds one user per role (login = role, e.g. "qa", "qam", "buyer", "admin"), a multi-role user
/// ("multi"), an employee with no role ("staff") and two vendors; password ChangeMe!2026.
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
            SeedEmployee("E:1", "admin", "Rohit Patel", [QpsRoles.Administrator]) with { Modules = [QpsRoles.ModuleUserManagement, QpsRoles.ModuleMenu] },
            SeedEmployee("E:2", "admin5", "Meera Iyer", [QpsRoles.Admin]),
            SeedEmployee("E:3", "qaadmin", "Neha Gupta", [QpsRoles.QAAdmin]),
            SeedEmployee("E:4", "qam", "Amit Sharma", [QpsRoles.QAM]),
            SeedEmployee("E:5", "qa", "Priya Rao", [QpsRoles.QA]),
            SeedEmployee("E:6", "ch", "Rakesh Verma", [QpsRoles.CategoryHead]),
            SeedEmployee("E:7", "ach", "Pooja Nair", [QpsRoles.AssociateCategoryHead]),
            SeedEmployee("E:8", "merch", "Karan Mehta", [QpsRoles.Merchandiser]),
            SeedEmployee("E:9", "buyer", "Sana Ali", [QpsRoles.Buyer]),
            SeedEmployee("E:10", "qc", "Vivek Singh", [QpsRoles.QC]),
            SeedEmployee("E:11", "view", "Anita Das", [QpsRoles.View]),
            SeedEmployee("E:12", "asn", "Manoj Kumar", [QpsRoles.ASN]),
            SeedEmployee("E:13", "bft", "Deepak Joshi", [QpsRoles.BFT]),
            SeedEmployee("E:14", "cm", "Ritu Malhotra", [QpsRoles.CM]),
            SeedEmployee("E:15", "multi", "Sanjay Rao", [QpsRoles.QAM, QpsRoles.QAAdmin]),
            SeedEmployee("E:16", "staff", "Arjun Menon", []),
            SeedVendor("V:1", "vendor1", "Sample Vendor Pvt Ltd") with { MustChangePassword = true },
            SeedVendor("V:2", "vendor2", "Kaveri Knitwear"),
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

    private static AuthUser SeedEmployee(string id, string login, string name, string[] roles) =>
        new(id, login, $"{login}@example.com", name, "", NewStamp(), IsActive: true, FailedLoginCount: 0,
            LockoutEndUtc: null, Roles: roles, AccountType.Employee);

    private static AuthUser SeedVendor(string id, string login, string name) =>
        new(id, login, $"{login}@example.com", name, "", NewStamp(), IsActive: true, FailedLoginCount: 0,
            LockoutEndUtc: null, Roles: [QpsRoles.Vendor], AccountType.Vendor);
}
