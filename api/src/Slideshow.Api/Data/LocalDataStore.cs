using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Slideshow.Api.Data;

/// <summary>
/// Local development and tests: the data lives in memory and, when Tables:LocalPath is set, is saved
/// to that JSON file after every change. Behaves like the table store: copies in and out, ETags on
/// albums, and inserts that fail when the key is taken.
/// </summary>
public sealed class LocalDataStore : IDataStore
{
    private sealed class State
    {
        public Dictionary<string, User> Users { get; set; } = [];
        public Dictionary<string, Album> Albums { get; set; } = [];       // key: owner/album
        public Dictionary<string, MediaFile> Media { get; set; } = [];    // key: owner/album/media
        public Dictionary<string, ShareRow> Shares { get; set; } = [];    // key: token
    }

    private sealed record ShareRow(string OwnerId, Guid AlbumId);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _gate = new();
    private readonly string? _path;
    private readonly State _s;

    public LocalDataStore(IOptions<TablesOptions> options)
    {
        var path = options.Value.LocalPath;
        _path = string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);
        _s = _path is not null && File.Exists(_path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(_path), Json) ?? new() : new();
    }

    private static string AlbumKey(string ownerId, Guid albumId) => $"{ownerId}/{albumId:N}";
    private static string MediaKey(string ownerId, Guid albumId, Guid mediaId) => $"{ownerId}/{albumId:N}/{mediaId:N}";

    private T Read<T>(Func<State, T> read) { lock (_gate) return read(_s); }

    private T Write<T>(Func<State, T> write)
    {
        lock (_gate)
        {
            var result = write(_s);
            if (_path is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_s, Json));
                File.Move(temp, _path, overwrite: true);
            }
            return result;
        }
    }

    private static string NextETag() => $"\"{Guid.NewGuid():N}\""; // unique across restarts too

    public Task<User?> GetUserAsync(string userId, CancellationToken ct) =>
        Task.FromResult(Read(s => s.Users.GetValueOrDefault(userId)?.Clone()));

    public Task<bool> TryAddUserAsync(User user, CancellationToken ct) =>
        Task.FromResult(Write(s => s.Users.TryAdd(user.Id, user.Clone())));

    public Task SetTokensValidAfterAsync(string userId, DateTime cutoff, CancellationToken ct) =>
        Task.FromResult(Write(s => { if (s.Users.TryGetValue(userId, out var u)) u.TokensValidAfter = cutoff; return 0; }));

    public Task DeleteUserAsync(string userId, CancellationToken ct) =>
        Task.FromResult(Write(s => s.Users.Remove(userId)));

    public Task<IReadOnlyList<Album>> ListAlbumsAsync(string ownerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Album>>(Read(s => s.Albums.Values.Where(a => a.OwnerId == ownerId).Select(a => a.Clone()).ToList()));

    public Task<Album?> GetAlbumAsync(string ownerId, Guid albumId, CancellationToken ct) =>
        Task.FromResult(Read(s => s.Albums.GetValueOrDefault(AlbumKey(ownerId, albumId))?.Clone()));

    public Task AddAlbumAsync(Album album, CancellationToken ct) =>
        Task.FromResult(Write(s =>
        {
            var copy = album.Clone();
            copy.ETag = NextETag();
            if (!s.Albums.TryAdd(AlbumKey(album.OwnerId, album.Id), copy)) throw new InvalidOperationException("Album already exists.");
            album.ETag = copy.ETag;
            return 0;
        }));

    public Task<bool> TryUpdateAlbumAsync(Album album, CancellationToken ct) =>
        Task.FromResult(Write(s =>
        {
            var key = AlbumKey(album.OwnerId, album.Id);
            if (!s.Albums.TryGetValue(key, out var stored) || stored.ETag != album.ETag) return false;
            var copy = album.Clone();
            copy.ETag = NextETag();
            s.Albums[key] = copy;
            album.ETag = copy.ETag;
            return true;
        }));

    public Task<bool> DeleteAlbumAsync(string ownerId, Guid albumId, CancellationToken ct) =>
        Task.FromResult(Write(s => s.Albums.Remove(AlbumKey(ownerId, albumId))));

    public Task<IReadOnlyList<MediaFile>> ListMediaAsync(string ownerId, Guid? albumId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MediaFile>>(Read(s => s.Media.Values
            .Where(m => m.OwnerId == ownerId && (albumId is null || m.AlbumId == albumId))
            .Select(m => m.Clone()).ToList()));

    public Task<MediaFile?> GetMediaAsync(string ownerId, Guid albumId, Guid mediaId, CancellationToken ct) =>
        Task.FromResult(Read(s => s.Media.GetValueOrDefault(MediaKey(ownerId, albumId, mediaId))?.Clone()));

    public Task AddMediaAsync(MediaFile media, CancellationToken ct) =>
        Task.FromResult(Write(s =>
        {
            if (!s.Media.TryAdd(MediaKey(media.OwnerId, media.AlbumId, media.Id), media.Clone())) throw new InvalidOperationException("Media already exists.");
            return 0;
        }));

    public Task<bool> DeleteMediaAsync(string ownerId, Guid albumId, Guid mediaId, CancellationToken ct) =>
        Task.FromResult(Write(s => s.Media.Remove(MediaKey(ownerId, albumId, mediaId))));

    public Task DeleteAllMediaAsync(string ownerId, Guid? albumId, CancellationToken ct) =>
        Task.FromResult(Write(s =>
        {
            foreach (var (key, m) in s.Media.ToList())
                if (m.OwnerId == ownerId && (albumId is null || m.AlbumId == albumId)) s.Media.Remove(key);
            return 0;
        }));

    public Task SetThumbnailStateAsync(string ownerId, Guid albumId, Guid mediaId, ThumbnailState state, CancellationToken ct) =>
        Task.FromResult(Write(s => { if (s.Media.TryGetValue(MediaKey(ownerId, albumId, mediaId), out var m)) m.ThumbnailState = state; return 0; }));

    public Task SetResizeStateAsync(string ownerId, Guid albumId, Guid mediaId, ResizeState state, IReadOnlyList<int> sizes, CancellationToken ct) =>
        Task.FromResult(Write(s =>
        {
            if (s.Media.TryGetValue(MediaKey(ownerId, albumId, mediaId), out var m)) { m.ResizeState = state; m.Sizes = [.. sizes]; }
            return 0;
        }));

    public Task<(string OwnerId, Guid AlbumId)?> ResolveShareAsync(string token, CancellationToken ct) =>
        Task.FromResult(Read<(string, Guid)?>(s => s.Shares.TryGetValue(token, out var r) ? (r.OwnerId, r.AlbumId) : null));

    public Task<bool> TryAddShareAsync(string token, string ownerId, Guid albumId, CancellationToken ct) =>
        Task.FromResult(Write(s => s.Shares.TryAdd(token, new ShareRow(ownerId, albumId))));

    public Task DeleteShareAsync(string token, CancellationToken ct) =>
        Task.FromResult(Write(s => s.Shares.Remove(token)));
}
