using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Admin.Auth;

public sealed record ResetPasswordResult(bool Succeeded, bool InvalidToken, IReadOnlyList<string> Errors)
{
    public static readonly ResetPasswordResult Success = new(true, false, []);
    public static readonly ResetPasswordResult BadToken = new(false, true, []);
}

public sealed class PasswordResetService(
    IAuthUserStore store,
    IAuthEmailSender email,
    IPasswordHasher<AuthUser> hasher,
    IOptions<AuthSettings> options,
    TimeProvider clock,
    ILogger<PasswordResetService> logger)
{
    /// <summary>
    /// Always completes the same way whether or not the account exists, so the page can
    /// show one message for both and nobody can use it to discover valid accounts.
    /// </summary>
    public async Task RequestResetAsync(string login, CancellationToken ct = default)
    {
        var settings = options.Value;
        var user = await store.FindByLoginAsync(login.Trim(), ct);
        if (user is not { IsActive: true })
        {
            logger.LogInformation("Password reset requested for an unknown or disabled login");
            return;
        }

        // The raw token only ever exists in the e-mail. The database keeps its SHA-256 hash,
        // so a leaked table can't be used to reset anyone's password.
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var validFor = TimeSpan.FromMinutes(settings.PasswordResetTokenMinutes);
        await store.SavePasswordResetTokenAsync(user.Id, Hash(token), clock.GetUtcNow() + validFor, ct);

        // Built from configuration, never from the request Host header (host-header poisoning).
        var link = $"{settings.PublicBaseUrl.TrimEnd('/')}/Account/Reset-Password?token={token}";
        await email.SendPasswordResetAsync(user, link, validFor, ct);
        logger.LogInformation("Password reset e-mail sent to {UserId}", user.Id);
    }

    public async Task<bool> IsTokenValidAsync(string? token, CancellationToken ct = default) =>
        IsWellFormed(token) && await store.FindUserIdByPasswordResetTokenAsync(Hash(token!), ct) is not null;

    public async Task<ResetPasswordResult> ResetPasswordAsync(string? token, string newPassword, CancellationToken ct = default)
    {
        if (!IsWellFormed(token))
            return ResetPasswordResult.BadToken;

        var tokenHash = Hash(token!);
        var userId = await store.FindUserIdByPasswordResetTokenAsync(tokenHash, ct);
        var user = userId is null ? null : await store.FindByIdAsync(userId, ct);
        if (user is not { IsActive: true })
            return ResetPasswordResult.BadToken;

        // Validate before consuming, so a weak password doesn't burn the link.
        var errors = PasswordPolicy.Validate(newPassword, user, options.Value.MinPasswordLength);
        if (errors.Count > 0)
            return new(false, false, errors);

        // Atomic: of two simultaneous submits only one gets the token.
        if (await store.ConsumePasswordResetTokenAsync(tokenHash, ct) != user.Id)
            return ResetPasswordResult.BadToken;

        // New security stamp => every existing session of this user is signed out.
        await store.SetPasswordHashAsync(user.Id, hasher.HashPassword(user, newPassword), rotateSecurityStamp: true, ct);
        await store.ClearLockoutAsync(user.Id, ct);
        logger.LogInformation("Password reset completed for {UserId}", user.Id);
        return ResetPasswordResult.Success;
    }

    private static bool IsWellFormed(string? token) => token is { Length: 64 } && token.All(char.IsAsciiHexDigit);

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token.ToUpperInvariant())));
}
