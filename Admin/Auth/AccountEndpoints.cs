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
        app.MapGet(AuthConstants.AntiforgeryTokenEndpoint, GetAntiforgeryToken).AllowAnonymous().ExcludeFromDescription();
        return app;
    }

    private static async Task<IResult> CompleteLoginAsync(
        HttpContext http, IAntiforgery antiforgery, LoginTicketStore tickets,
        IAuthUserStore store, LandingPageResolver landing, LoginNotices notices, TimeProvider clock, ILoggerFactory loggerFactory)
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

        // One active session per user, when ISingleSessionGuard is registered: start this session
        // and end the one on any other device. Done here, where the sign-in actually happens.
        var logger = loggerFactory.CreateLogger("Auth");
        StartedSession? session = null;
        if (http.RequestServices.GetService<ISingleSessionGuard>() is { } guard)
        {
            var client = new SessionClient(http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString());
            try
            {
                session = await guard.StartAsync(user, client, http.RequestAborted);
            }
            catch (Exception ex) when (!http.RequestAborted.IsCancellationRequested)
            {
                logger.LogError(ex, "Could not start a session for {UserId}", user.Id);
                return Results.LocalRedirect($"{AuthConstants.LoginPath}?error=unavailable&ReturnUrl={Uri.EscapeDataString(returnUrl)}");
            }
        }

        var principal = AuthClaims.CreatePrincipal(user, clock.GetUtcNow(), session?.Token);
        await http.SignInAsync(AuthConstants.Scheme, principal,
            new AuthenticationProperties { IsPersistent = ticket.RememberMe, AllowRefresh = true });

        // A pre-login server session must not carry over into the authenticated one.
        if (http.Features.Get<ISessionFeature>() is not null)
            http.Session.Clear();

        // Shown once on the first page (DashboardLayout): "you were signed in on another device".
        if (session is { EndedOtherSession: true })
        {
            notices.Add(session.Token, LoginNotices.DuplicateLogin);
            logger.LogInformation("User {UserId} signed in again; the session on the other device was ended", user.Id);
        }

        // Role/user-type dashboard, unless the user came from a specific protected page.
        var target = landing.AfterSignIn(principal, returnUrl);
        logger.LogInformation("User {UserId} signed in, landing on {Target}", user.Id, target);
        http.Response.Headers.CacheControl = "no-store";
        return Results.LocalRedirect(target);
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext http, IAntiforgery antiforgery, EndedSessions endedSessions, ILoggerFactory loggerFactory)
    {
        // POST + antiforgery: an <img src="/account/sign-out"> on another site can't sign users out.
        if (!await IsAntiforgeryValidAsync(http, antiforgery))
            return Results.BadRequest();

        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        // idle: timed out; replaced: signed in on another device; expired: session check failed.
        var reason = form["reason"].ToString() switch
        {
            "idle" => "idle",
            "replaced" => "replaced",
            "expired" => "expired",
            _ => "signedout",
        };
        var logger = loggerFactory.CreateLogger("Auth");

        var userId = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var tab = http.User.FindFirst(AuthClaimTypes.AccountType)?.Value == nameof(AccountType.Vendor) ? "&type=vendor" : "";

        if (SessionOwner.From(http.User) is { } session)
        {
            // A copy of this cookie (other tab, stolen) stops working at once, not after RevalidationMinutes.
            endedSessions.Add(session.Token, SessionEndReason.Expired);

            // End it in the session table too, unless it was already replaced by a newer sign-in
            // (ending that one would sign out the user's new device).
            if (reason != "replaced" && http.RequestServices.GetService<ISingleSessionGuard>() is { } guard)
            {
                try
                {
                    await guard.EndAsync(session, http.RequestAborted);
                }
                catch (Exception ex) when (!http.RequestAborted.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Could not end the session of {UserId}; signing out anyway", userId);
                }
            }
        }

        await http.SignOutAsync(AuthConstants.Scheme);
        if (http.Features.Get<ISessionFeature>() is not null)
            http.Session.Clear();

        logger.LogInformation("User {UserId} signed out ({Reason})", userId, reason);
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["Clear-Site-Data"] = "\"cache\"";  // drop cached authenticated pages
        return Results.LocalRedirect($"{AuthConstants.LoginPath}?reason={reason}{tab}");
    }

    /// <summary>
    /// An interactive page's &lt;AntiforgeryToken /&gt; is only filled in when the page was
    /// prerendered, so auth.js asks here right before posting instead. The response also sets
    /// the matching antiforgery cookie. Other sites can't read this response (same-origin
    /// policy), so they still can't forge the POST.
    /// </summary>
    private static IResult GetAntiforgeryToken(HttpContext http, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(http);
        http.Response.Headers.CacheControl = "no-store";
        return Results.Json(new { field = tokens.FormFieldName, token = tokens.RequestToken });
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
