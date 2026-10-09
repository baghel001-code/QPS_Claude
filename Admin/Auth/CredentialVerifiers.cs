namespace Admin.Auth;

public enum CredentialResult { Valid, Invalid, Unavailable }

/// <summary>
/// Checks a password for one account type. <see cref="AccountService"/> picks the verifier by
/// the tab the user signed in on; everything else (CAPTCHA, lockout, disabled check, ticket)
/// is the same for both types.
/// </summary>
public interface ICredentialVerifier
{
    AccountType AccountType { get; }

    /// <summary>
    /// Valid or Invalid for a definite answer. Unavailable when the answer couldn't be obtained
    /// (API down, timeout); that is not counted as a failed attempt.
    /// </summary>
    Task<CredentialResult> VerifyAsync(AuthUser user, string password, CancellationToken ct);
}

/// <summary>Vendors: the password hash is stored in the QPS database.</summary>
public sealed class VendorPasswordVerifier(AppPasswordHasher hasher, ILogger<VendorPasswordVerifier> logger) : ICredentialVerifier
{
    public AccountType AccountType => AccountType.Vendor;

    public Task<CredentialResult> VerifyAsync(AuthUser user, string password, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(user.PasswordHash))
            return Task.FromResult(CredentialResult.Invalid);   // no password set yet

        try
        {
            return Task.FromResult(hasher.VerifyPassword(password, user.PasswordHash)
                ? CredentialResult.Valid
                : CredentialResult.Invalid);
        }
        catch (Exception ex)
        {
            // A hash the hasher can't parse (corrupt row, old format) counts as a wrong password.
            logger.LogWarning(ex, "Stored password hash for {UserId} could not be verified", user.Id);
            return Task.FromResult(CredentialResult.Invalid);
        }
    }
}

/// <summary>
/// Employees: the password is checked by the company employee API (<see cref="IEmployeeAuthApi"/>).
/// QPS stores no employee password; the QPS user row only supplies roles, modules and status.
/// </summary>
public sealed class EmployeeApiVerifier(IEmployeeAuthApi api, ILogger<EmployeeApiVerifier> logger) : ICredentialVerifier
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public AccountType AccountType => AccountType.Employee;

    public async Task<CredentialResult> VerifyAsync(AuthUser user, string password, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        EmployeeApiUser? result;
        try
        {
            // The QPS user name (employee code), even if the user typed their e-mail.
            result = await api.AuthenticateAsync(user.UserName, password, timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // the page went away; not an API problem
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Employee authentication API failed for {UserId}", user.Id);
            return CredentialResult.Unavailable;
        }

        if (result is null)
            return CredentialResult.Invalid;

        // Only accept the answer if it is about the account being signed in to.
        if (!string.Equals(result.EmployeeCode?.Trim(), user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Employee API returned {Returned} for {UserId}; rejecting", result.EmployeeCode, user.Id);
            return CredentialResult.Invalid;
        }
        return CredentialResult.Valid;
    }
}
