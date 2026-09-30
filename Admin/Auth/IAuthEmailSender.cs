namespace Admin.Auth;

public interface IAuthEmailSender
{
    Task SendPasswordResetAsync(AuthUser user, string resetLink, TimeSpan validFor, CancellationToken ct = default);
}

/// <summary>DEVELOPMENT ONLY. Writes the reset link to the log instead of sending an e-mail.</summary>
public sealed class LoggingAuthEmailSender(ILogger<LoggingAuthEmailSender> logger, IHostEnvironment env) : IAuthEmailSender
{
    public Task SendPasswordResetAsync(AuthUser user, string resetLink, TimeSpan validFor, CancellationToken ct = default)
    {
        if (env.IsDevelopment())
            logger.LogWarning("DEV password reset link for {UserId}: {Link}", user.Id, resetLink);
        else
            logger.LogError("LoggingAuthEmailSender is registered outside Development; no reset e-mail was sent for {UserId}.", user.Id);
        return Task.CompletedTask;
    }
}
