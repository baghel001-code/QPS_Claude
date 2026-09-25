using Microsoft.AspNetCore.Authentication.Cookies;

namespace Admin.Auth;

public static class AuthConstants
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    // "__Host-" makes the browser reject the cookie unless it is Secure, Path=/ and has no Domain,
    // so a sibling sub-domain can never plant or overwrite it.
    public const string CookieName = "__Host-App.Auth";

    public const string LoginPath = "/Account/Login";
    public const string LogoutPagePath = "/Account/Logout";
    public const string AccessDeniedPath = "/Account/Access-Denied";

    // HTTP endpoints (AccountEndpoints) — the only places the cookie is written or cleared.
    public const string CompleteLoginEndpoint = "/account/complete-login";
    public const string LogoutEndpoint = "/account/logout";
}

public static class AuthClaimTypes
{
    public const string SecurityStamp = "sstamp";
    public const string AuthTime = "auth_time";     // unix seconds of the sign-in
    public const string DisplayName = "display_name";
    public const string AccountType = "account_type";   // "Employee" or "Vendor"
}
