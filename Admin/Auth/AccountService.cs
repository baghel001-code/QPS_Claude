using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Admin.Auth;

public enum LoginStatus { Success, InvalidCredentials, LockedOut, Disabled }

public sealed record LoginResult(LoginStatus Status, AuthUser? User = null);

/// <summary>
/// Checks credentials. Runs inside the Blazor circuit, so it never touches HttpContext;
/// the cookie is issued later by <see cref="AccountEndpoints"/>.
/// </summary>
public sealed class AccountService(
    IAuthUserStore store,
    IPasswordHasher<AuthUser> hasher,
    IOptions<AuthSettings> options,
    TimeProvider clock,
    ILogger<AccountService> logger)
{
    // Verified against when the user does not exist, so "unknown user" takes as long as
    // "wrong password" and response time does not reveal which usernames are real.
    private static readonly string DummyHash =
        new PasswordHasher<AuthUser>().HashPassword(null!, Guid.NewGuid().ToString());

    public async Task<LoginResult> ValidateCredentialsAsync(
        string login, string password, AccountType type, CancellationToken ct = default)
    {
        var settings = options.Value;
        login = login.Trim();

        var user = await store.FindByLoginAsync(login, type, ct);
        // The type check repeats the store's filter so a store bug can't let a vendor in through the employee tab.
        if (user is null || user.AccountType != type)
        {
            hasher.VerifyHashedPassword(null!, DummyHash, password);
            logger.LogInformation("Sign-in failed: unknown login");
            return new(LoginStatus.InvalidCredentials);
        }

        var now = clock.GetUtcNow();
        if (user.LockoutEndUtc > now)
        {
            logger.LogWarning("Sign-in refused: {UserId} is locked out until {Until}", user.Id, user.LockoutEndUtc);
            return new(LoginStatus.LockedOut);
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
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

        // Checked only after the password is right, so a guesser can't learn that the account is disabled.
        if (!user.IsActive)
        {
            logger.LogInformation("Sign-in refused: {UserId} is disabled", user.Id);
            return new(LoginStatus.Disabled);
        }

        if (user.FailedLoginCount > 0 || user.LockoutEndUtc is not null)
            await store.ClearLockoutAsync(user.Id, ct);

        // The hasher reports this when the stored hash uses older/weaker settings.
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            await store.SetPasswordHashAsync(user.Id, hasher.HashPassword(user, password), rotateSecurityStamp: false, ct);

        logger.LogInformation("Credentials verified for {UserId}", user.Id);
        return new(LoginStatus.Success, user);
    }
}
