using Slideshow.Api.Data;
using Slideshow.Api.Storage;

namespace Slideshow.Api.Media;

/// <summary>
/// One-off jobs for the move to the music library, run in the background each time the API starts. Once
/// they're done, starting up finds nothing to do (two quick queries):
/// - Deletes music users uploaded (rows and files), for everyone: music now comes only from the library,
///   whose licenses have been checked.
/// - Reads the running time of videos uploaded before it was recorded, for the trip page's "fits the music" table.
/// </summary>
public sealed class LibraryMigration(ISlideshowStore store, IBlobStore blobs, ILogger<LibraryMigration> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await RunAsync(store, blobs, log, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception e) { log.LogError(e, "The music library migration failed; it runs again at the next start"); }
    }

    /// <returns>How many uploaded tracks were deleted, and how many videos got their running time.</returns>
    public static async Task<(int MusicDeleted, int VideosTimed)> RunAsync(ISlideshowStore store, IBlobStore blobs, ILogger log, CancellationToken ct)
    {
        var uploads = await store.ListMediaAcrossOwnersAsync(MediaCollection.Music, null, ct);
        foreach (var m in uploads)
        {
            await store.DeleteMediaAsync(m.OwnerId, m.AlbumId, m.Id, ct); // the row first: then the file is unreachable
            await blobs.DeleteAsync(m.BlobName, ct);
        }
        if (uploads.Count > 0) log.LogWarning("Deleted {Count} uploaded music tracks (music now comes from the music library)", uploads.Count);

        var untimed = (await store.ListMediaAcrossOwnersAsync(MediaCollection.Images, MediaKind.Video, ct)).Where(m => m.DurationSeconds is null).ToList();
        foreach (var video in untimed)
        {
            double seconds = 0;
            try
            {
                await using var stream = await blobs.OpenReadAsync(video.BlobName, ct);
                if (stream is not null) seconds = MetadataReader.ReadDuration(stream);
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Couldn't read the running time of video {MediaId}", video.Id); }
            await store.SetDurationAsync(video.OwnerId, video.AlbumId, video.Id, seconds, ct); // 0: unknown, not tried again
        }
        if (untimed.Count > 0) log.LogInformation("Read the running time of {Count} videos", untimed.Count);
        return (uploads.Count, untimed.Count);
    }
}
