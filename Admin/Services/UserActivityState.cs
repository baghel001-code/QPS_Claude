namespace Admin.Services
{
    /// <summary>
    /// Per-circuit record of the user's last interaction with the UI.
    /// Registered as Scoped, so each Blazor circuit (browser tab) has its own instance.
    /// </summary>
    public interface IUserActivityState
    {
        DateTimeOffset LastActivityUtc { get; }
        TimeSpan IdleFor { get; }
        void Touch();
    }

    public sealed class UserActivityState : IUserActivityState
    {
        // Stored as ticks so reads/writes are atomic: Touch() is called from JS interop
        // while the revalidation timer reads it from a background thread.
        private long _lastActivityTicks = DateTimeOffset.UtcNow.UtcTicks;

        public DateTimeOffset LastActivityUtc => new(Interlocked.Read(ref _lastActivityTicks), TimeSpan.Zero);

        public TimeSpan IdleFor => DateTimeOffset.UtcNow - LastActivityUtc;

        public void Touch() => Interlocked.Exchange(ref _lastActivityTicks, DateTimeOffset.UtcNow.UtcTicks);
    }
}
