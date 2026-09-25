using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Admin.Auth;

public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers cookie authentication and the account services.
    /// You still register your own <see cref="IAuthUserStore"/> and <see cref="IAuthEmailSender"/>.
    /// </summary>
    public static IServiceCollection AddAppAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(AuthSettings.SectionName);
        services.AddOptions<AuthSettings>()
            .Bind(section)
            .Validate(s => Uri.TryCreate(s.PublicBaseUrl, UriKind.Absolute, out var u) &&
                           (u.Scheme == Uri.UriSchemeHttps || environment.IsDevelopment()),
                "Auth:PublicBaseUrl must be the site's absolute https address (used in reset e-mails).")
            .Validate(s => s.IdleTimeoutMinutes > 0 && s.AbsoluteLifetimeHours > 0 && s.MaxFailedAttempts > 0,
                "Auth timeouts and MaxFailedAttempts must be positive.")
            .ValidateOnStart();
        var settings = section.Get<AuthSettings>() ?? new AuthSettings();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPasswordHasher<AuthUser>, PasswordHasher<AuthUser>>(); // PBKDF2-SHA512, 100k iterations
        services.AddSingleton<LoginTicketStore>();
        services.AddScoped<AccountService>();
        services.AddScoped<PasswordResetService>();

        services.AddAuthentication(AuthConstants.Scheme)
            .AddCookie(AuthConstants.Scheme, o =>
            {
                o.Cookie.Name = AuthConstants.CookieName;
                o.Cookie.Path = "/";
                o.Cookie.HttpOnly = true;                           // not readable from JavaScript
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;  // required by the __Host- prefix
                o.Cookie.SameSite = SameSiteMode.Lax;               // Strict would drop it when arriving from an e-mail link

                o.LoginPath = AuthConstants.LoginPath;
                o.LogoutPath = AuthConstants.LogoutEndpoint;
                o.AccessDeniedPath = AuthConstants.AccessDeniedPath;

                o.ExpireTimeSpan = TimeSpan.FromMinutes(settings.IdleTimeoutMinutes);
                o.SlidingExpiration = true;

                o.Events.OnValidatePrincipal = CookieValidator.ValidatePrincipalAsync;
                // APIs get status codes, not a redirect to an HTML login page.
                o.Events.OnRedirectToLogin = ctx => RedirectOrStatus(ctx, StatusCodes.Status401Unauthorized);
                o.Events.OnRedirectToAccessDenied = ctx => RedirectOrStatus(ctx, StatusCodes.Status403Forbidden);
            });

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, AppRevalidatingAuthStateProvider>();
        return services;
    }

    private static Task RedirectOrStatus(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> ctx, int status)
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
            ctx.Response.StatusCode = status;
        else
            ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }
}
