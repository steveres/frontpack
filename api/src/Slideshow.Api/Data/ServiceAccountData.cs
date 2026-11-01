using Slideshow.Api.Storage;

namespace Slideshow.Api.Data;

/// <summary>What a user keeps in one service: counts by kind (e.g. "albums", "files") and bytes stored.</summary>
public sealed record ServiceUsage(IReadOnlyDictionary<string, long> Counts, long Bytes);

/// <summary>
/// A service's side of the FrontPack account: the account page totals its usage, and deleting the account
/// deletes its data. Each service keeps its own data; for now they all run in this API.
/// </summary>
public interface IServiceAccountData
{
    /// <summary>The service's name, e.g. "slideshow".</summary>
    string Service { get; }
    Task<ServiceUsage> GetUsageAsync(string userId, CancellationToken ct);
    /// <summary>Deletes everything the user keeps in the service. Safe to repeat after a failure.</summary>
    Task DeleteUserDataAsync(string userId, CancellationToken ct);
}

public sealed class SlideshowAccountData(ISlideshowStore store, IBlobStore blobs) : IServiceAccountData
{
    public string Service => "slideshow";

    public async Task<ServiceUsage> GetUsageAsync(string userId, CancellationToken ct)
    {
        var albums = await store.ListAlbumsAsync(userId, ct);
        var files = await store.ListMediaAsync(userId, null, ct);
        return new ServiceUsage(new Dictionary<string, long> { ["albums"] = albums.Count, ["files"] = files.Count }, files.Sum(m => m.SizeBytes));
    }

    public async Task DeleteUserDataAsync(string userId, CancellationToken ct)
    {
        // Media rows, then share links and albums, then the files: a retry after a failure picks up where it stopped.
        await store.DeleteAllMediaAsync(userId, null, ct);
        foreach (var album in await store.ListAlbumsAsync(userId, ct))
        {
            if (album.ShareToken is { } token) await store.DeleteShareAsync(token, ct);
            await store.DeleteAlbumAsync(userId, album.Id, ct);
        }
        await blobs.DeletePrefixAsync(BlobNames.UserPrefix(userId), ct);
    }
}
