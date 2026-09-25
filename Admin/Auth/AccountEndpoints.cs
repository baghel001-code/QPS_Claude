using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;

namespace Admin.Auth;

/// <summary>
/// The two real HTTP requests of the auth flow. The cookie can only be written in an HTTP
/// response, and an interactive page talks to the server over a WebSocket, so the pages
/// post a small form here with a full page load.
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous: the fallback policy must not block signing in, or signing out an expired session.
        app.MapPost(AuthConstants.CompleteLoginEndpoint, CompleteLoginAsync).AllowAnonymous().ExcludeFromDescription();
        app.MapPost(AuthConstants.LogoutEndpoint, LogoutAsync).AllowAnonymous().ExcludeFromDescription();
        return app;
    }

    private static async Task<IResult> CompleteLoginAsync(
        HttpContext http, IAntiforgery antiforgery, LoginTicketStore tickets,
        IAuthUserStore store, TimeProvider clock, ILoggerFactory loggerFactory)
    {
        // Antiforgery stops "login CSRF": another site can't post a ticket it obtained
        // for its own account and sign the victim in as the attacker.
        if (!await IsAntiforgeryValidAsync(http, antiforgery))
            return Results.BadRequest();

        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        var returnUrl = UrlSafety.LocalOrRoot(form["returnUrl"]);

        var ticket = tickets.Redeem(form["ticket"]);
        var user = ticket is null ? null : await store.FindByIdAsync(ticket.UserId, http.RequestAborted);
        if (ticket is null || user is not { IsActive: true })
            return Results.LocalRedirect($"{AuthConstants.LoginPath}?error=expired&ReturnUrl={Uri.EscapeDataString(returnUrl)}");

        await http.SignInAsync(AuthConstants.Scheme, AuthClaims.CreatePrincipal(user, clock.GetUtcNow()),
            new AuthenticationProperties { IsPersistent = ticket.RememberMe, AllowRefresh = true });

        // A pre-login server session must not carry over into the authenticated one.
        if (http.Features.Get<ISessionFeature>() is not null)
            http.Session.Clear();

        loggerFactory.CreateLogger("Auth").LogInformation("User {UserId} signed in", user.Id);
        http.Response.Headers.CacheControl = "no-store";
        return Results.LocalRedirect(returnUrl);
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, IAntiforgery antiforgery, ILoggerFactory loggerFactory)
    {
        // POST + antiforgery: an <img src="/account/logout"> on another site can't sign users out.
        if (!await IsAntiforgeryValidAsync(http, antiforgery))
            return Results.BadRequest();

        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        var reason = form["reason"] == "idle" ? "idle" : "signedout";

        var userId = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        await http.SignOutAsync(AuthConstants.Scheme);
        if (http.Features.Get<ISessionFeature>() is not null)
            http.Session.Clear();

        loggerFactory.CreateLogger("Auth").LogInformation("User {UserId} signed out ({Reason})", userId, reason);
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["Clear-Site-Data"] = "\"cache\"";  // drop cached authenticated pages
        return Results.LocalRedirect($"{AuthConstants.LoginPath}?reason={reason}");
    }

    private static async Task<bool> IsAntiforgeryValidAsync(HttpContext http, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(http);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
