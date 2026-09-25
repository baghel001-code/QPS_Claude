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
            await RejectAsync(context);
            return;
        }

        if (context.Properties.Items.TryGetValue(LastCheckedKey, out var raw) &&
            long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var unix) &&
            now - DateTimeOffset.FromUnixTimeSeconds(unix) < TimeSpan.FromMinutes(settings.RevalidationMinutes))
            return;

        var store = services.GetRequiredService<IAuthUserStore>();
        if (!await SessionValidation.IsValidAsync(principal, store, settings, now, context.HttpContext.RequestAborted))
        {
            await RejectAsync(context);
            return;
        }

        context.Properties.Items[LastCheckedKey] = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        context.ShouldRenew = true;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}
