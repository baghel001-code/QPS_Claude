using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Admin.Auth;

public sealed record LoginTicket(string UserId, bool RememberMe, DateTimeOffset ExpiresUtc);

/// <summary>
/// Hands a verified sign-in from the Blazor circuit (which cannot set cookies) to the
/// /account/complete-login HTTP endpoint (which can). Tickets are random, single-use and
/// expire after 60 seconds.
///
/// In-memory: fine for one server. Behind a load balancer, use sticky sessions (Blazor Server
/// needs them anyway) or move this to a shared cache such as Redis.
/// </summary>
public sealed class LoginTicketStore(TimeProvider clock)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, LoginTicket> _tickets = new();

    public string Issue(string userId, bool rememberMe)
    {
        var now = clock.GetUtcNow();
        foreach (var (key, t) in _tickets)
            if (t.ExpiresUtc <= now) _tickets.TryRemove(key, out _);

        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tickets[ticket] = new LoginTicket(userId, rememberMe, now + Lifetime);
        return ticket;
    }

    /// <summary>Returns the ticket once; a second call with the same value returns null.</summary>
    public LoginTicket? Redeem(string? ticket)
    {
        if (string.IsNullOrEmpty(ticket) || ticket.Length != 64)
            return null;
        return _tickets.TryRemove(ticket, out var t) && t.ExpiresUtc > clock.GetUtcNow() ? t : null;
    }
}
