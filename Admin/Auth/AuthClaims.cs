using System.Globalization;
using System.Security.Claims;

namespace Admin.Auth;

public static class AuthClaims
{
    public static ClaimsPrincipal CreatePrincipal(AuthUser user, DateTimeOffset signedInAt)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Email, user.Email),
            new(AuthClaimTypes.DisplayName, user.DisplayName),
            new(AuthClaimTypes.SecurityStamp, user.SecurityStamp),
            new(AuthClaimTypes.AuthTime, signedInAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthConstants.Scheme, ClaimTypes.Name, ClaimTypes.Role));
    }

    public static bool IsWithinAbsoluteLifetime(ClaimsPrincipal principal, AuthSettings settings, DateTimeOffset now) =>
        long.TryParse(principal.FindFirstValue(AuthClaimTypes.AuthTime), NumberStyles.None, CultureInfo.InvariantCulture, out var unix) &&
        now - DateTimeOffset.FromUnixTimeSeconds(unix) < TimeSpan.FromHours(settings.AbsoluteLifetimeHours);
}

/// <summary>Shared by the cookie handler (HTTP requests) and the circuit revalidator (open Blazor pages).</summary>
public static class SessionValidation
{
    public static async Task<bool> IsValidAsync(
        ClaimsPrincipal principal, IAuthUserStore store, AuthSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = principal.FindFirstValue(AuthClaimTypes.SecurityStamp);
        if (userId is null || stamp is null || !AuthClaims.IsWithinAbsoluteLifetime(principal, settings, now))
            return false;

        // Lockout is deliberately not checked: someone guessing a password must not be able
        // to kick the real user out of an existing session.
        var user = await store.FindByIdAsync(userId, ct);
        return user is { IsActive: true } && user.SecurityStamp == stamp;
    }
}
