using System.Globalization;
using System.Security.Claims;

namespace Admin.Auth;

public static class AuthClaims
{
    /// <param name="sessionId">The single-session token, or null to use a random id.</param>
    public static ClaimsPrincipal CreatePrincipal(AuthUser user, DateTimeOffset signedInAt, string? sessionId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Email, user.Email),
            new(AuthClaimTypes.DisplayName, user.DisplayName),
            new(AuthClaimTypes.SecurityStamp, user.SecurityStamp),
            new(AuthClaimTypes.AccountType, user.AccountType.ToString()),
            new(AuthClaimTypes.AuthTime, signedInAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            new(AuthClaimTypes.SessionId, string.IsNullOrEmpty(sessionId) ? Guid.NewGuid().ToString("N") : sessionId),
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(user.Modules.Select(m => new Claim(AuthClaimTypes.Module, m)));
        if (user.MustChangePassword)
            claims.Add(new Claim(AuthClaimTypes.MustChangePassword, "true"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthConstants.Scheme, ClaimTypes.Name, ClaimTypes.Role));
    }

    public static bool IsWithinAbsoluteLifetime(ClaimsPrincipal principal, AuthSettings settings, DateTimeOffset now) =>
        long.TryParse(principal.FindFirstValue(AuthClaimTypes.AuthTime), NumberStyles.None, CultureInfo.InvariantCulture, out var unix) &&
        now - DateTimeOffset.FromUnixTimeSeconds(unix) < TimeSpan.FromHours(settings.AbsoluteLifetimeHours);
}

/// <summary>Shared by the cookie handler (HTTP requests) and the circuit revalidator (open Blazor pages).</summary>
public static class SessionValidation
{
    /// <param name="guard">The single-session check, or null when that feature is off.</param>
    public static async Task<SessionEndReason> CheckAsync(
        ClaimsPrincipal principal, IAuthUserStore store, ISingleSessionGuard? guard,
        AuthSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = principal.FindFirstValue(AuthClaimTypes.SecurityStamp);
        if (userId is null || stamp is null || !AuthClaims.IsWithinAbsoluteLifetime(principal, settings, now))
            return SessionEndReason.Expired;

        // Lockout is deliberately not checked: someone guessing a password must not be able
        // to kick the real user out of an existing session.
        var user = await store.FindByIdAsync(userId, ct);
        if (user is not { IsActive: true } || user.SecurityStamp != stamp)
            return SessionEndReason.Expired;

        // Signed in on another device since: this session is no longer the active one.
        if (guard is not null && (SessionOwner.From(principal) is not { } session || !await guard.IsCurrentAsync(session, ct)))
            return SessionEndReason.Replaced;

        return SessionEndReason.None;
    }
}
