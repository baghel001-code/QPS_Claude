using Microsoft.Extensions.Options;

namespace Admin.Auth;

public enum LoginStatus { Success, InvalidCredentials, LockedOut, Disabled, ServiceUnavailable }

public sealed record LoginResult(LoginStatus Status, AuthUser? User = null);

/// <summary>
/// Checks credentials. Runs inside the Blazor circuit, so it never touches HttpContext;
/// the cookie is issued later by <see cref="AccountEndpoints"/>.
/// How the password itself is checked depends on the account type (<see cref="ICredentialVerifier"/>):
/// vendors against the hash in the QPS database, employees through the employee API.
/// </summary>
public sealed class AccountService(
    IAuthUserStore store,
    IEnumerable<ICredentialVerifier> verifiers,
    IOptions<AuthSettings> options,
    TimeProvider clock,
    ILogger<AccountService> logger)
{
    // Every failed attempt takes at least this long, so "unknown user" (no password check),
    // "wrong password" (hash or API call) and "not a QPS user" can't be told apart by timing.
    // It also slows down guessing.
    private static readonly TimeSpan MinimumFailureDuration = TimeSpan.FromSeconds(1);

    private readonly Dictionary<AccountType, ICredentialVerifier> _verifiers =
        verifiers.ToDictionary(v => v.AccountType);

    public async Task<LoginResult> ValidateCredentialsAsync(
        string login, string password, AccountType type, CancellationToken ct = default)
    {
        var started = clock.GetTimestamp();
        var result = await CheckAsync(login.Trim(), password, type, ct);
        if (result.Status is LoginStatus.InvalidCredentials)
        {
            var remaining = MinimumFailureDuration - clock.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, clock, ct);
        }
        return result;
    }

    private async Task<LoginResult> CheckAsync(string login, string password, AccountType type, CancellationToken ct)
    {
        var settings = options.Value;

        // 1. Find the QPS user on the chosen tab. For employees this is the QPS user row (roles,
        //    modules, status, lockout); the API is not called for people who aren't QPS users.
        var user = await store.FindByLoginAsync(login, type, ct);
        // The type check repeats the store's filter so a store bug can't let a vendor in through the employee tab.
        if (user is null || user.AccountType != type)
        {
            logger.LogInformation("Sign-in failed: unknown {Type} login", type);
            return new(LoginStatus.InvalidCredentials);
        }

        // 2. Locked out: refuse without checking the password (and without calling the API).
        var now = clock.GetUtcNow();
        if (user.LockoutEndUtc > now)
        {
            logger.LogWarning("Sign-in refused: {UserId} is locked out until {Until}", user.Id, user.LockoutEndUtc);
            return new(LoginStatus.LockedOut);
        }

        // 3. Check the password: vendor hash or employee API.
        if (!_verifiers.TryGetValue(type, out var verifier))
            throw new InvalidOperationException($"No ICredentialVerifier is registered for {type} accounts.");
        var check = await verifier.VerifyAsync(user, password, ct);

        if (check == CredentialResult.Unavailable)
        {
            // Not the user's fault: no failed attempt is recorded.
            logger.LogWarning("Sign-in not possible for {UserId}: {Type} credential service unavailable", user.Id, type);
            return new(LoginStatus.ServiceUnavailable);
        }

        // 4. Wrong password: count it, lock after MaxFailedAttempts.
        if (check == CredentialResult.Invalid)
        {
            var failures = await store.RecordFailedLoginAsync(user.Id, ct);
            if (failures >= settings.MaxFailedAttempts)
            {
                await store.LockOutAsync(user.Id, now.AddMinutes(settings.LockoutMinutes), ct);
                logger.LogWarning("Sign-in failed: {UserId} locked out after {Failures} attempts", user.Id, failures);
                return new(LoginStatus.LockedOut);
            }
            logger.LogInformation("Sign-in failed: wrong password for {UserId} ({Failures})", user.Id, failures);
            return new(LoginStatus.InvalidCredentials);
        }

        // 5. Disabled in QPS. Checked only after the password is right, so a guesser can't learn it.
        if (!user.IsActive)
        {
            logger.LogInformation("Sign-in refused: {UserId} is disabled", user.Id);
            return new(LoginStatus.Disabled);
        }

        // 6. Success: reset the failure counter.
        if (user.FailedLoginCount > 0 || user.LockoutEndUtc is not null)
            await store.ClearLockoutAsync(user.Id, ct);

        logger.LogInformation("Credentials verified for {UserId}", user.Id);
        return new(LoginStatus.Success, user);
    }
}
