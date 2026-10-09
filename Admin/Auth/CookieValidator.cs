using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Admin.Auth;

/// <summary>
/// Runs on every HTTP request that carries the auth cookie (page loads, API calls, the
/// Blazor connection). Enforces the absolute lifetime on each request and, at most every
/// RevalidationMinutes, re-checks the user in the store.
/// </summary>
internal static class CookieValidator
{
    private const string LastCheckedKey = ".app.checked";

    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is not { Identity.IsAuthenticated: true } principal)
            return;

        var services = context.HttpContext.RequestServices;
        var settings = services.GetRequiredService<IOptions<AuthSettings>>().Value;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();

        if (!AuthClaims.IsWithinAbsoluteLifetime(principal, settings, now))
        {
            await RejectAsync(context, SessionEndReason.Expired);
            return;
        }

        // Already found ended by an open page's check (in memory, so checked on every request).
        if (services.GetRequiredService<EndedSessions>().TryGet(principal.FindFirst(AuthClaimTypes.SessionId)?.Value, out var ended))
        {
            await RejectAsync(context, ended);
            return;
        }

        if (context.Properties.Items.TryGetValue(LastCheckedKey, out var raw) &&
            long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var unix) &&
            now - DateTimeOffset.FromUnixTimeSeconds(unix) < TimeSpan.FromMinutes(settings.RevalidationMinutes))
            return;

        var store = services.GetRequiredService<IAuthUserStore>();
        var guard = services.GetService<ISingleSessionGuard>();
        SessionEndReason reason;
        try
        {
            reason = await SessionValidation.CheckAsync(principal, store, guard, settings, now, context.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            // DB/API down: let this request through and try again on the next one (the time of the
            // last successful check is not updated). Open pages give up after 5 failures in a row.
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CookieValidator))
                .LogWarning(ex, "Session check failed; trying again on the next request");
            return;
        }
        if (reason != SessionEndReason.None)
        {
            await RejectAsync(context, reason);
            return;
        }

        context.Properties.Items[LastCheckedKey] = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        context.ShouldRenew = true;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context, SessionEndReason reason)
    {
        // Shown on the login page as ?reason=replaced / expired (see AuthServiceCollectionExtensions).
        context.HttpContext.Items[AuthConstants.EndReasonItem] = SessionEndReasons.ToQuery(reason);
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}
