namespace Admin.Auth;

/// <summary>
/// NIST SP 800-63B style: length and a block-list instead of forced symbol/upper-case rules.
/// Extend <see cref="Blocked"/> (or check a breached-password list) for stronger protection.
/// </summary>
public static class PasswordPolicy
{
    public const int MaxLength = 128;

    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "password@123", "passw0rd",
        "123456789", "1234567890", "12345678910", "qwerty123", "qwertyuiop", "1q2w3e4r5t",
        "welcome123", "welcome@123", "admin123", "admin@123", "letmein123", "iloveyou",
        "changeme", "changeme123",
    };

    public static IReadOnlyList<string> Validate(string? password, AuthUser user, int minLength)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Enter a password.");
            return errors;
        }
        if (password.Length < minLength)
            errors.Add($"Use at least {minLength} characters.");
        if (password.Length > MaxLength)
            errors.Add($"Use at most {MaxLength} characters.");
        if (password.Distinct().Count() < 4)
            errors.Add("Use more varied characters.");
        if (Blocked.Contains(password))
            errors.Add("This password is too common.");
        if (Contains(password, user.UserName) || Contains(password, user.Email.Split('@')[0]))
            errors.Add("Don't include your username or e-mail in the password.");
        return errors;
    }

    private static bool Contains(string password, string part) =>
        part.Length >= 3 && password.Contains(part, StringComparison.OrdinalIgnoreCase);
}
