using System.Collections.Concurrent;

namespace Slideshow.Api.Data;

/// <summary>
/// Notes when each user last used Trip Pix, for the site admins' overview. A user's time is written at most
/// once an hour by each API replica (it remembers what it wrote), so a busy session costs one table write an
/// hour. A failed write is logged and forgotten: it never gets in the user's way.
/// </summary>
public sealed class LastSeenTracker(ISlideshowStore store, ILogger<LastSeenTracker> log)
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<string, DateTime> _written = new();

    public async Task SeenAsync(string userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (_written.TryGetValue(userId, out var last) && now - last < Interval) return;
        _written[userId] = now; // before the write: requests arriving meanwhile don't write it too
        try { await store.SetLastSeenAsync(userId, now, ct); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _written.TryRemove(userId, out _); // try again on the next request
            log.LogWarning(e, "Couldn't record when user {UserId} was last seen", userId);
        }
    }
}
