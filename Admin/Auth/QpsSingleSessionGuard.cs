using Application.Interfaces.V1.User;
using Domain.Entities.Request;

namespace Admin.Auth;

/// <summary>
/// One session per user, using the existing session API (IUserValidationService:
/// ACTIVATE / VALIDATE / CLEAR_USER_SESSION). Switch it on in Program.cs:
///     builder.Services.AddScoped&lt;ISingleSessionGuard, QpsSingleSessionGuard&gt;();
/// </summary>
public sealed class QpsSingleSessionGuard(
    IUserValidationService sessions,
    ILogger<QpsSingleSessionGuard> logger) : ISingleSessionGuard
{
    // The USER_TYPE values the session table uses.
    private const string VendorType = "VENDOR";
    private const string EmployeeType = "EMPLOYEE";

    public async Task<StartedSession> StartAsync(AuthUser user, SessionClient client, CancellationToken ct)
    {
        var request = Request(user.Id, user.UserName, user.AccountType, token: null, await DescribeAsync(client));

        // Asked before issuing: issuing replaces the old token, after which nothing is "active" any more.
        var hadExistingSession = await sessions.HasActiveSessionAsync(request, ct);
        var token = await sessions.IssueNewSessionAsync(request, ct);
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("ACTIVATE_USER_SESSION returned no session token.");   // → "temporarily unavailable"

        return new StartedSession(token, hadExistingSession);
    }

    public async Task<bool> IsCurrentAsync(SessionOwner session, CancellationToken ct)
    {
        try
        {
            return await sessions.IsSessionValidAsync(Request(session, clientInfo: null), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // API down or an empty response: keep the user signed in until the next check,
            // instead of signing everyone out on one failed call.
            logger.LogWarning(ex, "Session check failed for {UserId}; trying again at the next check", session.UserId);
            return true;
        }
    }

    public Task EndAsync(SessionOwner session, CancellationToken ct) =>
        sessions.ClearSessionAsync(Request(session, clientInfo: null), ct);

    private static IssueSessionRequest Request(SessionOwner session, string? clientInfo) =>
        Request(session.UserId, session.UserName, session.AccountType, session.Token, clientInfo);

    private static IssueSessionRequest Request(string authUserId, string userCode, AccountType type, string? token, string? clientInfo)
    {
        if (!UserLoginRecord.TryParseId(authUserId, out _, out var id))   // "V:88" → 88
            throw new InvalidOperationException($"Unexpected user id '{authUserId}'.");
        return new IssueSessionRequest(type == AccountType.Vendor ? VendorType : EmployeeType, userCode, checked((int)id), token, clientInfo);
    }

    /// <summary>"ip / host", as the old login stored it.</summary>
    private static async Task<string> DescribeAsync(SessionClient client)
    {
        var host = "";
        if (System.Net.IPAddress.TryParse(client.IpAddress, out var ip))
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));   // reverse DNS can hang
                host = (await System.Net.Dns.GetHostEntryAsync(ip.ToString(), timeout.Token)).HostName;
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException)
            {
            }
        }
        return $"{client.IpAddress} / {host}";
    }
}
