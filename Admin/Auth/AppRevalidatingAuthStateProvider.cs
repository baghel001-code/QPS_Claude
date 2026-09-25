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
    TimeProvider clock) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(options.Value.RevalidationMinutes);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
    {
        if (state.User.Identity?.IsAuthenticated != true)
            return true;

        // The provider lives as long as the circuit; use a fresh scope so a scoped DbContext isn't held open.
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuthUserStore>();
        return await SessionValidation.IsValidAsync(state.User, store, options.Value, clock.GetUtcNow(), ct);
    }
}
