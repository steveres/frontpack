using Slideshow.Api.Storage;

namespace Slideshow.Api.Data;

/// <summary>Slideshow's side of the FrontPack account: its albums and files count towards the usage, and go with the account.</summary>
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
