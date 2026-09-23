using Application.Interfaces.V1.User;
using Application.Services.User;
using Domain.Entities.AppSettings;
using Domain.Entities.Common;
using Domain.Entities.Request;
using Domain.Entities.Response;
using Domain.Enums;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using System.Security.Claims;

namespace Admin.Services
{
    public class CustomRevalidatingAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
    {
        // After this many back-to-back failures (DB/API unreachable) we stop trusting the old
        // result and sign the user out. With a 60 s interval that is ~5 minutes of outage.
        private const int MaxConsecutiveFailures = 5;
        private const int MinRevalidateSeconds = 5;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISessionInvalidationState _invalidationState;
        private readonly IUserActivityState _activityState;
        private readonly AppConfigurationSettings _settings;
        private readonly ILogger _logger;
        private int _consecutiveFailures;

        // How often to revalidate. Each run costs 2 service/DB calls per open tab, so keep it
        // in the 30-300 s range (AppConfigurationSettings:UserRevalidateInSeconds).
        protected override TimeSpan RevalidationInterval =>
            TimeSpan.FromSeconds(Math.Max(_settings.UserRevalidateInSeconds, MinRevalidateSeconds));

        private TimeSpan IdleTimeout => TimeSpan.FromMinutes(Math.Max(_settings.MaxSessionTime, 1));

        public CustomRevalidatingAuthenticationStateProvider(
            ILoggerFactory loggerFactory,
            IServiceScopeFactory scopeFactory,
            ISessionInvalidationState invalidationState,
            IUserActivityState activityState,
            AppConfigurationSettings settings) : base(loggerFactory)
        {
            _scopeFactory = scopeFactory;
            _invalidationState = invalidationState;
            _activityState = activityState;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = loggerFactory.CreateLogger<CustomRevalidatingAuthenticationStateProvider>();
        }

        protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
        {
            var user = authenticationState.User;
            if (user?.Identity?.IsAuthenticated != true)
                return false;

            // Idle check first: in-memory, no DB call. IdleTimeoutMonitor.razor normally logs the
            // user out (with a warning) before this fires; this is the server-side backstop in case
            // the monitor isn't rendered or JS isn't running.
            if (_activityState.IdleFor >= IdleTimeout)
            {
                _invalidationState.Reason = SessionInvalidReason.IdleTimeout;
                return false;
            }

            try
            {
                // Runs on a background timer tied to the circuit — needs its own DI scope
                // because the circuit's scoped services (like a DbContext) shouldn't be
                // reused concurrently from a timer thread.
                await using var scope = _scopeFactory.CreateAsyncScope();
                var isValid = await ValidateUserAsync(scope.ServiceProvider, user, cancellationToken);
                _consecutiveFailures = 0;
                return isValid;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // Circuit is shutting down.
                throw;
            }
            catch (Exception ex)
            {
                // The base class signs the user out on ANY exception, so a single DB/API timeout
                // would log users out at random. Tolerate transient failures for a while instead.
                _consecutiveFailures++;
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _logger.LogError(ex, "Session revalidation failed {Count} times in a row; signing user out.", _consecutiveFailures);
                    _invalidationState.Reason = SessionInvalidReason.Expired;
                    return false;
                }

                _logger.LogWarning(ex, "Session revalidation failed ({Count}/{Max}); keeping user signed in until the next check.",
                    _consecutiveFailures, MaxConsecutiveFailures);
                return true;
            }
        }

        private async Task<bool> ValidateUserAsync(IServiceProvider serviceProvider, ClaimsPrincipal user, CancellationToken cancellationToken)
        {
            // Absolute expiry claim issued at login (if any). Note: this is a fixed lifetime from
            // login, not an idle timeout — claims inside a circuit never slide.
            var expClaim = user.FindFirst("exp")?.Value;
            if (!string.IsNullOrEmpty(expClaim) && long.TryParse(expClaim, out var expUnix))
            {
                var expiry = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                if (expiry < DateTimeOffset.UtcNow)
                {
                    _invalidationState.Reason = SessionInvalidReason.Expired;
                    return false;
                }
            }

            // Check against DB if the user is still active / not force-logged-out
            var sessionToken = user.FindFirst(CustomClaimTypes.SessionToken)?.Value;
            var uidClaim = user.FindFirst(CustomClaimTypes.User_Id)?.Value;
            if (string.IsNullOrEmpty(sessionToken) || string.IsNullOrEmpty(uidClaim) || !int.TryParse(uidClaim, out var userId))
            {
                _invalidationState.Reason = SessionInvalidReason.Expired;
                return false;
            }

            var USER_CODE = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            var userType = user.FindFirst(CustomClaimTypes.UserType)?.Value;

            //-- Check User Emp Status and Resignation
            SelectListReq selectListReq = new SelectListReq
            {
                Id = userId,
                StrField = USER_CODE,
                Cmd = CmdNames.Get_User_Details_On_User_Code
            };

            var userValidationService = serviceProvider.GetRequiredService<IUserValidationService>();
            var issueSessionRequest = new IssueSessionRequest(userType ?? "", USER_CODE, userId, sessionToken);

            var isSessionValid = await userValidationService.IsSessionValidAsync(issueSessionRequest, cancellationToken);
            if (isSessionValid)
            {
                var isStillValid = await userValidationService.IsUserActiveAsync(selectListReq, cancellationToken);
                isSessionValid = isSessionValid && isStillValid;
            }

            if (!isSessionValid)
            {
                // Resigned / deactivated — an admin-side change, group with forced logout
                _invalidationState.Reason = SessionInvalidReason.ForcedLogout;
                return false;
            }

            return true;
        }
    }
}
