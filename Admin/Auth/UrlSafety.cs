namespace Admin.Auth;

public static class UrlSafety
{
    /// <summary>Shown when a ReturnUrl was rejected because it isn't a page on this site.</summary>
    public const string RejectedPath = "/nopage";

    /// <summary>
    /// Where to go after signing in:
    /// <list type="bullet">
    /// <item>no ReturnUrl, or a login/logout page (which would loop) → "/" (home)</item>
    /// <item>a path on this site → that path</item>
    /// <item>anything else → <see cref="RejectedPath"/></item>
    /// </list>
    /// Stops open redirects such as ReturnUrl=https://evil.example or ReturnUrl=//evil.example.
    /// </summary>
    public static string LocalOrRoot(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "/";
        if (!IsLocal(url))
            return RejectedPath;
        // Returning to the login/logout pages after signing in would be a loop.
        if (url.StartsWith("/Account/Log", StringComparison.OrdinalIgnoreCase))
            return "/";
        return url;
    }

    public static bool IsLocal(string url) =>
        url.Length > 0 && url[0] == '/'
        // "//host" and "/\host" are treated as absolute URLs by browsers.
        && !(url.Length > 1 && (url[1] == '/' || url[1] == '\\'))
        && !url.Any(char.IsControl);
}
