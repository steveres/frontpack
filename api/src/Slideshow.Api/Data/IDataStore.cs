namespace Slideshow.Api.Data;

/// <summary>
/// Users, albums, media details and share links: Azure Table Storage in the cloud, memory (optionally
/// saved to a file) locally and in tests. Everything is keyed by owner, so each query reads one partition.
/// Reads return copies: changing one changes nothing until it is written back.
/// </summary>
public interface IDataStore
{
    Task<User?> GetUserAsync(string userId, CancellationToken ct);
    /// <summary>Adds the user; false if one with that id already exists.</summary>
    Task<bool> TryAddUserAsync(User user, CancellationToken ct);
    /// <summary>Logout: tokens issued before `cutoff` are rejected. Nothing happens for an unknown user.</summary>
    Task SetTokensValidAfterAsync(string userId, DateTime cutoff, CancellationToken ct);
    Task DeleteUserAsync(string userId, CancellationToken ct);

    /// <summary>The owner's albums, in no particular order.</summary>
    Task<IReadOnlyList<Album>> ListAlbumsAsync(string ownerId, CancellationToken ct);
    Task<Album?> GetAlbumAsync(string ownerId, Guid albumId, CancellationToken ct);
    Task AddAlbumAsync(Album album, CancellationToken ct);
    /// <summary>Writes the album back if it hasn't changed since it was read (its ETag); false if it has, or is gone.</summary>
    Task<bool> TryUpdateAlbumAsync(Album album, CancellationToken ct);
    /// <summary>Deletes the album row only (not its media); false if there was none.</summary>
    Task<bool> DeleteAlbumAsync(string ownerId, Guid albumId, CancellationToken ct);

    /// <summary>The owner's media, all of it or one album's, in no particular order.</summary>
    Task<IReadOnlyList<MediaFile>> ListMediaAsync(string ownerId, Guid? albumId, CancellationToken ct);
    Task<MediaFile?> GetMediaAsync(string ownerId, Guid albumId, Guid mediaId, CancellationToken ct);
    Task AddMediaAsync(MediaFile media, CancellationToken ct);
    /// <summary>False if there was none.</summary>
    Task<bool> DeleteMediaAsync(string ownerId, Guid albumId, Guid mediaId, CancellationToken ct);
    /// <summary>Deletes the owner's media rows: one album's, or all of them.</summary>
    Task DeleteAllMediaAsync(string ownerId, Guid? albumId, CancellationToken ct);
    Task SetThumbnailStateAsync(string ownerId, Guid albumId, Guid mediaId, ThumbnailState state, CancellationToken ct);
    /// <summary>Records the photo's smaller versions. Nothing happens if it has been deleted.</summary>
    Task SetResizeStateAsync(string ownerId, Guid albumId, Guid mediaId, ResizeState state, IReadOnlyList<int> sizes, CancellationToken ct);

    /// <summary>The album a share code points to, or null.</summary>
    Task<(string OwnerId, Guid AlbumId)?> ResolveShareAsync(string token, CancellationToken ct);
    /// <summary>Claims a share code for the album; false if the code is already taken.</summary>
    Task<bool> TryAddShareAsync(string token, string ownerId, Guid albumId, CancellationToken ct);
    Task DeleteShareAsync(string token, CancellationToken ct);
}

public static class DataStoreExtensions
{
    /// <summary>
    /// Reads the album, applies `change` and writes it back, starting again if someone else changed it
    /// meanwhile. Returns the album as written, or null if it doesn't exist.
    /// </summary>
    public static async Task<Album?> UpdateAlbumAsync(this IDataStore store, string ownerId, Guid albumId, Action<Album> change, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var album = await store.GetAlbumAsync(ownerId, albumId, ct);
            if (album is null) return null;
            change(album);
            if (await store.TryUpdateAlbumAsync(album, ct)) return album;
            if (attempt == 20) throw new InvalidOperationException($"Album {albumId} kept changing; gave up updating it.");
        }
    }

    /// <summary>Marks the album's contents changed (media added or deleted), so its manifest is stale.</summary>
    public static Task<Album?> BumpVersionAsync(this IDataStore store, string ownerId, Guid albumId, DateTime now, CancellationToken ct) =>
        store.UpdateAlbumAsync(ownerId, albumId, a => { a.ContentVersion++; a.UpdatedAt = now; }, ct);
}
