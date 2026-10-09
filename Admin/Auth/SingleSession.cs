using System.Collections.Concurrent;
using System.Security.Claims;

namespace Admin.Auth;

/// <summary>Who is signing in, for the session log ("ip / host" in the old login).</summary>
public sealed record SessionClient(string? IpAddress, string? UserAgent);

/// <param name="Token">The new session's token; stored in the cookie as the session id.</param>
/// <param name="EndedOtherSession">True when the user was already signed in somewhere else (now ended).</param>
public sealed record StartedSession(string Token, bool EndedOtherSession);

/// <summary>The session a cookie belongs to, read from its claims.</summary>
public sealed record SessionOwner(string UserId, string UserName, AccountType AccountType, string Token)
{
    public static SessionOwner? From(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = user.FindFirstValue(ClaimTypes.Name);
        var token = user.FindFirstValue(AuthClaimTypes.SessionId);
        var typeOk = Enum.TryParse<AccountType>(user.FindFirstValue(AuthClaimTypes.AccountType), out var type);
        return id is null || name is null || token is null || !typeOk ? null : new(id, name, type, token);
    }
}

/// <summary>
/// One active session per user (no sign-in on two devices at once). Optional: register an
/// implementation in Program.cs to switch it on. Wrap your existing session-token service:
///   StartAsync    → HasActiveSessionAsync + IssueNewSessionAsync   (on sign-in)
///   IsCurrentAsync→ IsSessionValidAsync                            (every RevalidationMinutes)
///   EndAsync      → your "end session" call, for THIS token only    (on sign-out)
/// </summary>
public interface ISingleSessionGuard
{
    /// <summary>Starts a new session and ends any other session of this user.</summary>
    Task<StartedSession> StartAsync(AuthUser user, SessionClient client, CancellationToken ct);

    /// <summary>
    /// False when this token is no longer the user's active session (signed in elsewhere).
    /// On a temporary failure (DB/API down) return true, or users get signed out at random.
    /// </summary>
    Task<bool> IsCurrentAsync(SessionOwner session, CancellationToken ct);

    /// <summary>Ends this session (sign-out). Must not end a newer session of the same user.</summary>
    Task EndAsync(SessionOwner session, CancellationToken ct);
}

public enum SessionEndReason { None, Replaced, Expired }

/// <summary>Per circuit: why the session check signed the user out (set by AppRevalidatingAuthStateProvider).</summary>
public sealed class SessionEndState
{
    public SessionEndReason Reason { get; set; }
}

/// <summary>
/// Sessions found ended while a page was open. The cookie handler normally re-checks only every
/// RevalidationMinutes; this list makes it refuse such a cookie on the very next request.
/// In memory: per server (with several servers, sticky sessions keep a user on one).
/// </summary>
public sealed class EndedSessions(TimeProvider clock)
{
    private static readonly TimeSpan Keep = TimeSpan.FromHours(24);   // longer than any cookie lives
    private readonly ConcurrentDictionary<string, (SessionEndReason Reason, DateTimeOffset Until)> _ended = new();

    public void Add(string? sessionId, SessionEndReason reason)
    {
        if (string.IsNullOrEmpty(sessionId))
            return;
        var now = clock.GetUtcNow();
        foreach (var (key, entry) in _ended)
            if (entry.Until <= now) _ended.TryRemove(key, out _);
        _ended[sessionId] = (reason, now + Keep);
    }

    public bool TryGet(string? sessionId, out SessionEndReason reason)
    {
        reason = SessionEndReason.None;
        if (string.IsNullOrEmpty(sessionId) || !_ended.TryGetValue(sessionId, out var entry) || entry.Until <= clock.GetUtcNow())
            return false;
        reason = entry.Reason;
        return true;
    }
}

/// <summary>
/// One-time messages for the first page after sign-in (replaces Session["LoginNotice"], which an
/// interactive page can't read). Keyed by the new session id, kept for 2 minutes.
/// </summary>
public sealed class LoginNotices(TimeProvider clock)
{
    public const string DuplicateLogin = "DuplicateLogin";

    private static readonly TimeSpan Keep = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, (string Notice, DateTimeOffset Until)> _notices = new();

    public void Add(string sessionId, string notice)
    {
        var now = clock.GetUtcNow();
        foreach (var (key, entry) in _notices)
            if (entry.Until <= now) _notices.TryRemove(key, out _);
        _notices[sessionId] = (notice, now + Keep);
    }

    /// <summary>Returns the notice once; later calls return null.</summary>
    public string? Take(string? sessionId) =>
        !string.IsNullOrEmpty(sessionId) && _notices.TryRemove(sessionId, out var entry) && entry.Until > clock.GetUtcNow()
            ? entry.Notice
            : null;
}
