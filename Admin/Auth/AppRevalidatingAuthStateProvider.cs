using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Admin.Services;
using Microsoft.Extensions.Options;

namespace Admin.Auth;

/// <summary>
/// An open interactive page makes no HTTP requests, so the cookie handler never sees it.
/// This re-checks the circuit's user every RevalidationMinutes; when the check fails the page
/// becomes anonymous, [Authorize] content is hidden and DashboardLayout signs the user out.
/// Checks, in order: idle (in memory), then user/stamp/single session (store and API).
/// </summary>
public sealed class AppRevalidatingAuthStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<AuthSettings> options,
    TimeProvider clock,
    SessionEndState endState,
    EndedSessions endedSessions,
    IUserActivityState activity) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    // After this many failed checks in a row (DB/API unreachable) the user is signed out.
    // Until then a failed check keeps the user signed in: one timeout must not log everyone out.
    private const int MaxConsecutiveFailures = 5;

    private readonly ILogger _logger = loggerFactory.CreateLogger<AppRevalidatingAuthStateProvider>();
    private int _consecutiveFailures;   // checks run one at a time per circuit

    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(options.Value.RevalidationMinutes);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
    {
        if (state.User.Identity?.IsAuthenticated != true)
            return true;

        // Server-side idle backstop. IdleTimeoutMonitor (browser) normally signs the user out first,
        // with a warning; this catches the case where its JavaScript isn't running.
        if (activity.IdleFor >= TimeSpan.FromMinutes(options.Value.IdleTimeoutMinutes))
            return End(state, SessionEndReason.Idle);

        SessionEndReason reason;
        try
        {
            // The provider lives as long as the circuit; use a fresh scope so a scoped DbContext isn't held open.
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IAuthUserStore>();
            var guard = scope.ServiceProvider.GetService<ISingleSessionGuard>();
            reason = await SessionValidation.CheckAsync(state.User, store, guard, options.Value, clock.GetUtcNow(), ct);
            _consecutiveFailures = 0;
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            throw;   // circuit is closing
        }
        catch (Exception ex)
        {
            // The base class signs the user out on any exception; tolerate a short outage instead.
            _consecutiveFailures++;
            if (_consecutiveFailures >= MaxConsecutiveFailures)
            {
                _logger.LogError(ex, "Session check failed {Count} times in a row; signing the user out", _consecutiveFailures);
                return End(state, SessionEndReason.Expired);
            }
            _logger.LogWarning(ex, "Session check failed ({Count}/{Max}); keeping the user signed in until the next check",
                _consecutiveFailures, MaxConsecutiveFailures);
            return true;
        }

        return reason == SessionEndReason.None || End(state, reason);
    }

    private bool End(AuthenticationState state, SessionEndReason reason)
    {
        // DashboardLayout reads the reason and posts the sign-out form; until then, the cookie
        // is refused on its next HTTP request (EndedSessions), not only after RevalidationMinutes.
        endState.Reason = reason;
        endedSessions.Add(state.User.FindFirst(AuthClaimTypes.SessionId)?.Value, reason);
        return false;
    }
}
