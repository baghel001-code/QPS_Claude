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
    // Their paths must not equal a page route: routing ignores case, so "/account/logout"
    // would collide with the /Account/Logout page and throw AmbiguousMatchException.
    public const string CompleteLoginEndpoint = "/account/complete-login";
    public const string LogoutEndpoint = "/account/sign-out";

    // GET: returns a fresh antiforgery token (and sets its cookie) for the two POSTs above.
    // Used by wwwroot/js/auth.js so the forms work whether or not the page was prerendered.
    public const string AntiforgeryTokenEndpoint = "/account/antiforgery-token";

    // Browsers cache JS modules loaded with import() very aggressively; bump the version
    // whenever wwwroot/js/auth.js changes so every user gets the new file.
    public const string AuthScriptUrl = "./js/auth.js?v=2026092902";
}

public static class AuthClaimTypes
{
    public const string SecurityStamp = "sstamp";
    public const string AuthTime = "auth_time";     // unix seconds of the sign-in
    public const string DisplayName = "display_name";
    public const string AccountType = "account_type";   // "Employee" or "Vendor"
}
