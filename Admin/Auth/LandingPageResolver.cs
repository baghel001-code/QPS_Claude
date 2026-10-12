using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Admin.Auth;

/// <summary>
/// Where a signed-in user lands. Everyone goes to the home page "/", which shows the dashboard
/// for their user type and role (Components/Pages/Home.razor). The one exception: users who
/// must change their password go to the change-password page first.
/// </summary>
public sealed class LandingPageResolver(IOptions<AuthSettings> options, ILogger<LandingPageResolver> logger)
{
    public const string Home = "/";

    public string For(ClaimsPrincipal user)
    {
        if (user.FindFirst(AuthClaimTypes.MustChangePassword)?.Value != "true")
            return Home;

        var path = options.Value.LandingPages.ChangePassword;
        if (!string.IsNullOrWhiteSpace(path) && UrlSafety.IsLocal(path))
            return path;
        logger.LogError("Auth:LandingPages:ChangePassword is not a local path ('{Path}'); using /", path);
        return Home;
    }

    /// <summary>
    /// After sign-in: change-password first if required; otherwise a ReturnUrl from a protected
    /// page wins (the user asked for that page), and without one ("/") they go home.
    /// </summary>
    public string AfterSignIn(ClaimsPrincipal user, string safeReturnUrl)
    {
        var landing = For(user);
        return landing != Home ? landing : safeReturnUrl;
    }
}
