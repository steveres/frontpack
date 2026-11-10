namespace Slideshow.Api.Data;

/// <summary>
/// The music library, kept in memory for a short while: every slideshow, trip and public listing reads it, and it
/// changes only when a music admin edits it (which refreshes it here at once; other API replicas within a minute).
/// </summary>
public sealed class MusicLibrary(ISlideshowStore store)
{
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(60);
    private (DateTime ReadAt, IReadOnlyDictionary<Guid, MusicTrack> Tracks)? _cache;

    public async Task<IReadOnlyDictionary<Guid, MusicTrack>> GetAsync(CancellationToken ct)
    {
        if (_cache is { } c && DateTime.UtcNow - c.ReadAt < Fresh) return c.Tracks;
        var tracks = (await store.ListTracksAsync(ct)).ToDictionary(t => t.Id);
        _cache = (DateTime.UtcNow, tracks);
        return tracks;
    }

    /// <summary>After a change: the next read goes to the store.</summary>
    public void Invalidate() => _cache = null;

    /// <summary>The album's music in play order, skipping tracks no longer in the library.</summary>
    public async Task<IReadOnlyList<TrackDto>> TracksForAsync(Album album, CancellationToken ct) =>
        TracksFor(album, await GetAsync(ct));

    public static IReadOnlyList<TrackDto> TracksFor(Album album, IReadOnlyDictionary<Guid, MusicTrack> library) =>
        album.MusicTrackIds.Select(id => library.GetValueOrDefault(id)).OfType<MusicTrack>().Select(TrackDto.From).ToList();

    public static int CountFor(Album album, IReadOnlyDictionary<Guid, MusicTrack> library) =>
        album.MusicTrackIds.Count(library.ContainsKey);
}
