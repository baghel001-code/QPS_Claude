namespace Admin.Auth;

/// <summary>What the employee API returns for a correct user name and password.</summary>
/// <param name="EmployeeCode">The employee's login / code; must match the QPS user name.</param>
public sealed record EmployeeApiUser(string EmployeeCode, string? Name = null, string? Email = null);

/// <summary>
/// The company API that checks employee passwords (two parameters in, the user out).
/// Implement it with your existing API client and register it in Program.cs:
///     builder.Services.AddScoped&lt;IEmployeeAuthApi, QpsEmployeeAuthApi&gt;();
/// </summary>
public interface IEmployeeAuthApi
{
    /// <summary>
    /// Returns the employee when the user name and password are correct, and null when they are not.
    /// Throw (or let the HTTP client throw) when the API can't be reached or returns an error:
    /// that shows "temporarily unavailable" and is not counted as a failed attempt.
    /// </summary>
    Task<EmployeeApiUser?> AuthenticateAsync(string userName, string password, CancellationToken ct = default);
}

/// <summary>
/// DEVELOPMENT ONLY. Accepts the password ChangeMe!2026 for any employee, like InMemoryAuthUserStore.
/// </summary>
public sealed class DevEmployeeAuthApi(IHostEnvironment env, ILogger<DevEmployeeAuthApi> logger) : IEmployeeAuthApi
{
    public Task<EmployeeApiUser?> AuthenticateAsync(string userName, string password, CancellationToken ct = default)
    {
        if (!env.IsDevelopment())
        {
            logger.LogError("DevEmployeeAuthApi is registered outside Development; employee sign-in refused.");
            throw new InvalidOperationException("DevEmployeeAuthApi must not be used outside Development.");
        }
        return Task.FromResult(password == "ChangeMe!2026" ? new EmployeeApiUser(userName) : null);
    }
}
