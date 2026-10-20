using Microsoft.Extensions.Options;
using Slideshow.Api.Data;
using Slideshow.Api.Storage;

namespace Slideshow.Api.Media;

/// <summary>
/// Makes the smaller versions of uploaded photos, one photo at a time, from the resize queue. A photo
/// that keeps failing is given up on after Resize:MaxAttempts tries and stays full size only.
/// </summary>
public sealed class ResizeWorker(IResizeQueue queue, IDataStore store, IBlobStore blobs, IOptions<ResizeOptions> options, ILogger<ResizeWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            QueuedJob? job;
            try
            {
                job = await queue.ReceiveAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                log.LogWarning(e, "Couldn't read the resize queue; trying again shortly");
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
                continue;
            }
            if (job is not null) await HandleAsync(job, stoppingToken);
        }
    }

    /// <summary>Does one job, then finishes it, retries it later, or (after too many tries) gives up on the photo.</summary>
    public async Task HandleAsync(QueuedJob job, CancellationToken ct)
    {
        var (ownerId, albumId, mediaId) = job.Job;
        try
        {
            await ResizeAsync(job.Job, ct);
            await queue.CompleteAsync(job, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Stopping: the queue hands the job out again later.
        }
        catch (Exception e)
        {
            try
            {
                if (job.Attempt >= options.Value.MaxAttempts)
                {
                    log.LogError(e, "Gave up making smaller versions of {MediaId} after {Attempts} tries", mediaId, job.Attempt);
                    await store.SetResizeStateAsync(ownerId, albumId, mediaId, ResizeState.Unavailable, [], ct);
                    await queue.CompleteAsync(job, ct);
                }
                else
                {
                    log.LogWarning(e, "Couldn't make smaller versions of {MediaId} (try {Attempt}); will try again", mediaId, job.Attempt);
                    await queue.RetryLaterAsync(job, RetryDelay(job.Attempt), ct);
                }
            }
            catch (Exception again) when (again is not OperationCanceledException)
            {
                // Azure hands the job out again anyway once its time runs out.
                log.LogWarning(again, "Couldn't record the failure for {MediaId}", mediaId);
            }
        }
    }

    /// <summary>30 s, 1 min, 2 min, 4 min, …</summary>
    private static TimeSpan RetryDelay(int attempt) => TimeSpan.FromSeconds(30 * Math.Pow(2, Math.Min(attempt - 1, 6)));

    private async Task ResizeAsync(ResizeJob job, CancellationToken ct)
    {
        var media = await store.GetMediaAsync(job.OwnerId, job.AlbumId, job.MediaId, ct);
        // Deleted, not a photo, or done already (a photo can be queued more than once).
        if (media is null || media.Kind != MediaKind.Image || media.ResizeState is ResizeState.Ready or ResizeState.Unavailable) return;

        // Read it all first (photos are at most Limits:MaxImageBytes), so decoding doesn't wait on the network.
        using var original = new MemoryStream();
        await using (var stream = await blobs.OpenReadAsync(media.BlobName, ct))
        {
            if (stream is null) return; // deleted meanwhile
            await stream.CopyToAsync(original, ct);
        }
        original.Position = 0;

        var made = Renditions.TryCreate(original);
        if (made is null)
        {
            // Can't be decoded here (e.g. AVIF), damaged, or animated: it's shown full size.
            await store.SetResizeStateAsync(media.OwnerId, media.AlbumId, media.Id, ResizeState.Unavailable, [], ct);
            log.LogInformation("No smaller versions for {MediaId}: not a still photo this server can read", media.Id);
            return;
        }

        foreach (var (size, jpeg) in made)
            await blobs.UploadAsync(BlobNames.Sized(media.BlobName, size), new MemoryStream(jpeg), Renditions.ContentType, ct);
        var sizes = made.Select(m => m.Size).ToList();
        await store.SetResizeStateAsync(media.OwnerId, media.AlbumId, media.Id, ResizeState.Ready, sizes, ct);

        // Deleted while we worked: the delete may have run before our files existed, so take them back out.
        if (await store.GetMediaAsync(media.OwnerId, media.AlbumId, media.Id, ct) is null)
        {
            foreach (var size in sizes) await blobs.DeleteAsync(BlobNames.Sized(media.BlobName, size), CancellationToken.None);
            return;
        }
        log.LogInformation("Made {Count} smaller versions of {MediaId}", sizes.Count, media.Id);
    }
}
