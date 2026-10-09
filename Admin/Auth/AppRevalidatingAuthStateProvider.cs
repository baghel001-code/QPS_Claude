using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Options;

namespace Admin.Auth;

/// <summary>
/// An open interactive page makes no HTTP requests, so the cookie handler never sees it.
/// This re-checks the circuit's user on a timer; when the check fails the page becomes
/// anonymous and [Authorize] content is hidden straight away.
/// </summary>
public sealed class AppRevalidatingAuthStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<AuthSettings> options,
    TimeProvider clock,
    SessionEndState endState,
    EndedSessions endedSessions) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(options.Value.RevalidationMinutes);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
    {
        if (state.User.Identity?.IsAuthenticated != true)
            return true;

        // The provider lives as long as the circuit; use a fresh scope so a scoped DbContext isn't held open.
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuthUserStore>();
        var guard = scope.ServiceProvider.GetService<ISingleSessionGuard>();
        var reason = await SessionValidation.CheckAsync(state.User, store, guard, options.Value, clock.GetUtcNow(), ct);
        if (reason == SessionEndReason.None)
            return true;

        // DashboardLayout reads the reason and posts the sign-out form; until then, the cookie
        // is refused on its next HTTP request (EndedSessions), not only after RevalidationMinutes.
        endState.Reason = reason;
        endedSessions.Add(state.User.FindFirst(AuthClaimTypes.SessionId)?.Value, reason);
        return false;
    }
}
