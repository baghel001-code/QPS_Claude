namespace Admin.Auth;

/// <summary>Bound from the "Auth" section of appsettings.json.</summary>
public sealed class AuthSettings
{
    public const string SectionName = "Auth";

    /// <summary>Public https address used in e-mailed links. Never taken from the request Host header.</summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>Sliding idle timeout of the auth cookie.</summary>
    public int IdleTimeoutMinutes { get; set; } = 30;

    /// <summary>Hard limit after sign-in, even for an active user.</summary>
    public int AbsoluteLifetimeHours { get; set; } = 12;

    /// <summary>How often an open session re-checks the user in the store (disabled, password changed).</summary>
    public int RevalidationMinutes { get; set; } = 5;

    /// <summary>
    /// "Keep me signed in": idle limit for users who tick it (cookie survives closing the browser).
    /// Set RememberMeIdleTimeoutHours or RememberMeAbsoluteLifetimeDays to 0 to hide the checkbox.
    /// </summary>
    public int RememberMeIdleTimeoutHours { get; set; } = 8;

    /// <summary>"Keep me signed in": hard limit after sign-in, even for an active user.</summary>
    public int RememberMeAbsoluteLifetimeDays { get; set; } = 7;

    public bool RememberMeEnabled => RememberMeIdleTimeoutHours > 0 && RememberMeAbsoluteLifetimeDays > 0;

    public TimeSpan IdleTimeoutFor(bool rememberMe) =>
        rememberMe && RememberMeEnabled ? TimeSpan.FromHours(RememberMeIdleTimeoutHours) : TimeSpan.FromMinutes(IdleTimeoutMinutes);

    public TimeSpan AbsoluteLifetimeFor(bool rememberMe) =>
        rememberMe && RememberMeEnabled ? TimeSpan.FromDays(RememberMeAbsoluteLifetimeDays) : TimeSpan.FromHours(AbsoluteLifetimeHours);

    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;

    public int PasswordResetTokenMinutes { get; set; } = 30;
    public int MinPasswordLength { get; set; } = 10;

    /// <summary>Where users land after signing in. See LandingPageResolver.</summary>
    public LandingPageSettings LandingPages { get; set; } = new();
}

/// <summary>"Auth:LandingPages" in appsettings.json. Everyone else lands on "/".</summary>
public sealed class LandingPageSettings
{
    /// <summary>Users who must change their password go here first. Must start with "/".</summary>
    public string ChangePassword { get; set; } = "/changepassword";
}
