namespace Admin.Auth;

/// <summary>
/// Data access for authentication. Implement this against your database;
/// <see cref="InMemoryAuthUserStore"/> is a development stand-in.
/// </summary>
public interface IAuthUserStore
{
    /// <summary>Case-insensitive match on user name OR e-mail.</summary>
    Task<AuthUser?> FindByLoginAsync(string userNameOrEmail, CancellationToken ct = default);

    Task<AuthUser?> FindByIdAsync(string userId, CancellationToken ct = default);

    /// <summary>Increments the failed-login counter and returns the new value.</summary>
    Task<int> RecordFailedLoginAsync(string userId, CancellationToken ct = default);

    /// <summary>Locks the account until <paramref name="untilUtc"/> and resets the counter.</summary>
    Task LockOutAsync(string userId, DateTimeOffset untilUtc, CancellationToken ct = default);

    /// <summary>Resets the counter and removes any lockout.</summary>
    Task ClearLockoutAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Saves a new hash. When <paramref name="rotateSecurityStamp"/> is true, also sets a new
    /// SecurityStamp, which signs the user out everywhere.
    /// </summary>
    Task SetPasswordHashAsync(string userId, string passwordHash, bool rotateSecurityStamp, CancellationToken ct = default);

    /// <summary>Stores the SHA-256 hash of a reset token, replacing any earlier token for the user.</summary>
    Task SavePasswordResetTokenAsync(string userId, string tokenHash, DateTimeOffset expiresUtc, CancellationToken ct = default);

    /// <summary>Returns the user id for an unexpired token without using it up.</summary>
    Task<string?> FindUserIdByPasswordResetTokenAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Atomically deletes an unexpired token and returns its user id (null if none).</summary>
    Task<string?> ConsumePasswordResetTokenAsync(string tokenHash, CancellationToken ct = default);
}
