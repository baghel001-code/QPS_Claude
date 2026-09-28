using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Admin.Auth.Captcha;

/// <summary>
/// Issues and checks image CAPTCHAs. The answer never leaves the server: the page gets an id
/// and a PNG. Each challenge is single-use (checked once, right or wrong) and expires after
/// 5 minutes.
///
/// In-memory, like LoginTicketStore: fine for one server or sticky sessions.
/// </summary>
public sealed class CaptchaService(TimeProvider clock)
{
    public const int Length = 5;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const int PruneThreshold = 10_000;

    private readonly ConcurrentDictionary<string, Entry> _challenges = new();

    private sealed record Entry(string Answer, DateTimeOffset ExpiresUtc);

    public sealed record Challenge(string Id, string ImageDataUrl);

    public Challenge Create()
    {
        var now = clock.GetUtcNow();
        if (_challenges.Count > PruneThreshold)
            foreach (var (key, e) in _challenges)
                if (e.ExpiresUtc <= now) _challenges.TryRemove(key, out _);

        var answer = RandomCode();
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _challenges[id] = new Entry(answer, now + Lifetime);

        var png = CaptchaImage.RenderPng(answer);
        return new Challenge(id, "data:image/png;base64," + Convert.ToBase64String(png));
    }

    /// <summary>
    /// True if <paramref name="typed"/> matches (case-insensitive, spaces ignored).
    /// Uses the challenge up either way, so every attempt needs a fresh image.
    /// </summary>
    public bool Validate(string? id, string? typed)
    {
        if (string.IsNullOrEmpty(id) || !_challenges.TryRemove(id, out var entry))
            return false;
        if (entry.ExpiresUtc <= clock.GetUtcNow())
            return false;

        var normalized = new string((typed ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        return normalized.Length == entry.Answer.Length &&
               CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(normalized), Encoding.UTF8.GetBytes(entry.Answer));
    }

    public void Discard(string? id)
    {
        if (id is not null)
            _challenges.TryRemove(id, out _);
    }

    private static string RandomCode()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++)
            chars[i] = CaptchaImage.Alphabet[RandomNumberGenerator.GetInt32(CaptchaImage.Alphabet.Length)];
        return new string(chars);
    }
}
