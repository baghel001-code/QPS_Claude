namespace Admin.Auth;

public static class UrlSafety
{
    /// <summary>
    /// Returns <paramref name="url"/> only if it is a path on this site; otherwise "/".
    /// Stops open redirects such as ReturnUrl=https://evil.example or ReturnUrl=//evil.example.
    /// </summary>
    public static string LocalOrRoot(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url[0] != '/')
            return "/";
        // "//host" and "/\host" are treated as absolute URLs by browsers.
        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\'))
            return "/";
        if (url.Any(char.IsControl))
            return "/";
        // Returning to the login/logout pages after signing in would be a loop.
        if (url.StartsWith("/Account/Log", StringComparison.OrdinalIgnoreCase))
            return "/";
        return url;
    }
}
