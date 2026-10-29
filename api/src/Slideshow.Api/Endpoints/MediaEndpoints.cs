using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Slideshow.Api.Data;
using Slideshow.Api.Identity;
using Slideshow.Api.Media;
using Slideshow.Api.Storage;

namespace Slideshow.Api.Endpoints;

/// <summary>/albums/{albumId}/images (photos and videos) and /albums/{albumId}/music share these handlers.</summary>
public static class MediaEndpoints
{
    private const long MultipartOverhead = 64 * 1024; // boundaries, part headers and the small form fields

    public static string ContentUrl(Guid albumId, string segment, Guid id) => $"/api/v1/albums/{albumId}/{segment}/{id}";

    public static void MapCollection(RouteGroupBuilder albums, string segment, MediaCollection collection, long maxUploadBytes)
    {
        var g = albums.MapGroup($"/{{albumId:guid}}/{segment}").WithTags(collection == MediaCollection.Images ? "Images" : "Music");
        var what = collection == MediaCollection.Images ? "image or video" : "music track";

        g.MapGet("/", (Guid albumId, HttpContext http, IDataStore store, CancellationToken ct) => List(albumId, segment, collection, http, store, ct))
            .WithSummary($"Every {what} in the album, in upload order.");

        g.MapPost("/", (Guid albumId, IFormFile? file, [FromForm] int? utcOffsetMinutes,
                HttpContext http, IDataStore store, IBlobStore blobs, IResizeQueue queue, IOptions<LimitsOptions> limits, ILogger<Program> log, CancellationToken ct) =>
                Add(albumId, segment, collection, file, utcOffsetMinutes, http, store, blobs, queue, limits.Value, log, ct))
            .WithSummary($"Upload an {what} (multipart/form-data, field \"file\").")
            .DisableAntiforgery() // bearer tokens only, no cookies: nothing for CSRF to ride on
            .Accepts<IFormFile>("multipart/form-data")
            .WithMetadata(new RequestSizeLimitAttribute(maxUploadBytes + MultipartOverhead))
            .WithFormOptions(multipartBodyLengthLimit: maxUploadBytes + MultipartOverhead);

        g.MapGet("/{id:guid}", (Guid albumId, Guid id, int? size, HttpContext http, IDataStore store, IBlobStore blobs, IResizeQueue queue, ILogger<Program> log, CancellationToken ct) =>
                GetContent(albumId, id, size, collection, http, store, blobs, queue, log, ct))
            .WithSummary(collection == MediaCollection.Images
                ? "The file's bytes (supports Range). Photos: ?size=720, 1280 or 1920 for a smaller version where there is one."
                : $"The {what}'s bytes (supports Range).");

        g.MapGet("/{id:guid}/info", async (Guid albumId, Guid id, HttpContext http, IDataStore store, CancellationToken ct) =>
                await Find(store, http.User.UserId(), albumId, collection, id, ct) is { } m
                    ? Results.Ok(MediaInfoDto.From(m, ContentUrl(albumId, segment, m.Id)))
                    : Problems.MediaNotFound())
            .WithSummary($"The {what}'s details.");

        g.MapDelete("/{id:guid}", (Guid albumId, Guid id, HttpContext http, IDataStore store, IBlobStore blobs, ILogger<Program> log, CancellationToken ct) =>
                Delete(albumId, id, collection, http, store, blobs, log, ct))
            .WithSummary($"Delete the {what}.");

        g.MapPost("/delete", (Guid albumId, DeleteMediaRequest body, HttpContext http, IDataStore store, IBlobStore blobs, ILogger<Program> log, CancellationToken ct) =>
                DeleteMany(albumId, body, collection, http, store, blobs, log, ct))
            .WithSummary($"Delete several files at once (body: {{ \"ids\": [...] }}); ids not in the album are skipped.");

        if (collection == MediaCollection.Images)
            g.MapGet("/{id:guid}/thumbnail", (Guid albumId, Guid id, HttpContext http, IDataStore store, IBlobStore blobs, CancellationToken ct) =>
                    GetThumbnail(albumId, id, http, store, blobs, ct))
                .WithSummary("A small JPEG preview of a photo (videos have none yet).");
    }

    private static async Task<MediaFile?> Find(IDataStore store, string ownerId, Guid albumId, MediaCollection collection, Guid id, CancellationToken ct) =>
        await store.GetMediaAsync(ownerId, albumId, id, ct) is { } m && m.Collection == collection ? m : null;

    private static async Task<bool> AlbumExists(IDataStore store, string ownerId, Guid albumId, CancellationToken ct) =>
        await store.GetAlbumAsync(ownerId, albumId, ct) is not null;

    private static async Task<IResult> List(Guid albumId, string segment, MediaCollection collection, HttpContext http, IDataStore store, CancellationToken ct)
    {
        var ownerId = http.User.UserId();
        if (!await AlbumExists(store, ownerId, albumId, ct)) return Problems.AlbumNotFound();
        var media = (await store.ListMediaAsync(ownerId, albumId, ct))
            .Where(m => m.Collection == collection)
            .OrderBy(m => m.UploadedAt).ThenBy(m => m.Id);
        return Results.Ok(media.Select(m => MediaInfoDto.From(m, ContentUrl(albumId, segment, m.Id))));
    }

    private static async Task<IResult> Add(Guid albumId, string segment, MediaCollection collection, IFormFile? file, int? utcOffsetMinutes,
        HttpContext http, IDataStore store, IBlobStore blobs, IResizeQueue queue, LimitsOptions limits, ILogger log, CancellationToken ct)
    {
        var ownerId = http.User.UserId();
        if (!await AlbumExists(store, ownerId, albumId, ct)) return Problems.AlbumNotFound();
        if (file is null || file.Length == 0) return Problems.Validation("Send the file as multipart/form-data in a field named \"file\".");
        if (utcOffsetMinutes is < -14 * 60 or > 14 * 60) return Problems.Validation("utcOffsetMinutes must be between -840 and 840.");

        await using var content = file.OpenReadStream(); // buffered by ASP.NET Core (to disk when large), so seekable
        var header = new byte[MediaSniffer.HeaderLength];
        var headerLength = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var (sniffed, rejection) = MediaSniffer.Sniff(header.AsSpan(0, headerLength));
        if (sniffed is null) return Problems.Of(415, "unsupported_media_type", rejection!);

        var fits = collection == MediaCollection.Music ? sniffed.Kind == MediaKind.Audio : sniffed.Kind != MediaKind.Audio;
        if (!fits) return Problems.Of(415, "unsupported_media_type",
            collection == MediaCollection.Music ? "Music must be MP3." : "Upload music to the album's music collection.");

        var max = sniffed.Kind switch { MediaKind.Image => limits.MaxImageBytes, MediaKind.Video => limits.MaxVideoBytes, _ => limits.MaxMusicBytes };
        if (file.Length > max) return Problems.Of(413, "file_too_large", $"This kind of file can be at most {max / (1024 * 1024)} MB.");

        var owned = await store.ListMediaAsync(ownerId, null, ct);
        if (owned.Sum(m => m.SizeBytes) + file.Length > limits.QuotaBytesPerUser) return Problems.Of(413, "quota_exceeded", "Your storage is full. Delete some files and try again.");
        if (owned.Count(m => m.AlbumId == albumId) >= limits.MaxFilesPerAlbum)
            return Problems.Of(409, "album_full", $"An album can hold at most {limits.MaxFilesPerAlbum} files.");

        var offset = TimeSpan.FromMinutes(utcOffsetMinutes ?? 0);
        var meta = collection == MediaCollection.Images
            ? MetadataReader.Read(content, sniffed.Kind, offset)
            : new MediaMetadata(null, null, null, null);

        byte[]? thumbnail = null;
        if (sniffed.Kind == MediaKind.Image)
        {
            content.Position = 0;
            thumbnail = Thumbnails.TryCreate(content);
        }

        var now = DateTime.UtcNow;
        var media = new MediaFile
        {
            Id = Guid.CreateVersion7(),
            AlbumId = albumId,
            OwnerId = ownerId,
            Collection = collection,
            Kind = sniffed.Kind,
            FileName = SafeFileName(file.FileName),
            ContentType = sniffed.ContentType,
            SizeBytes = file.Length,
            UploadedAt = now,
            TakenAt = meta.TakenAt,
            TakenSource = meta.TakenSource,
            Latitude = meta.Latitude,
            Longitude = meta.Longitude,
            ThumbnailState = sniffed.Kind != MediaKind.Image ? ThumbnailState.None
                : thumbnail is null ? ThumbnailState.Unavailable : ThumbnailState.Ready,
            // Smaller versions are made in the background; a photo with no preview can't be read here, so it won't get them either.
            ResizeState = sniffed.Kind != MediaKind.Image ? ResizeState.None
                : thumbnail is null ? ResizeState.Unavailable : ResizeState.Pending,
            BlobName = "",
        };
        media.BlobName = BlobNames.For(ownerId, albumId, media.Id);

        content.Position = 0;
        await blobs.UploadAsync(media.BlobName, content, media.ContentType, ct);
        try
        {
            if (thumbnail is not null)
                await blobs.UploadAsync(BlobNames.Thumbnail(media.BlobName), new MemoryStream(thumbnail), Thumbnails.ContentType, ct);
            await store.AddMediaAsync(media, ct);
            if (await store.BumpVersionAsync(ownerId, albumId, now, ct) is null)
            {
                // The album was deleted mid-upload: take the file back out.
                await store.DeleteMediaAsync(ownerId, albumId, media.Id, CancellationToken.None);
                await DeleteBlobsAsync();
                return Problems.AlbumNotFound();
            }
        }
        catch
        {
            await DeleteBlobsAsync();
            throw;
        }

        async Task DeleteBlobsAsync()
        {
            await blobs.DeleteAsync(media.BlobName, CancellationToken.None);
            if (thumbnail is not null) await blobs.DeleteAsync(BlobNames.Thumbnail(media.BlobName), CancellationToken.None);
        }
        log.LogInformation("Stored {Kind} {MediaId} ({Bytes} bytes) in album {AlbumId}", media.Kind, media.Id, media.SizeBytes, albumId);
        if (media.ResizeState == ResizeState.Pending && !await TryQueueResizeAsync(queue, media, log))
        {
            // Not queued: asking for a size later queues it again.
            await store.SetResizeStateAsync(ownerId, albumId, media.Id, ResizeState.None, [], CancellationToken.None);
            media.ResizeState = ResizeState.None;
        }
        var url = ContentUrl(albumId, segment, media.Id);
        return Results.Created(url + "/info", MediaInfoDto.From(media, url));
    }

    private static async Task<IResult> GetContent(Guid albumId, Guid id, int? size, MediaCollection collection, HttpContext http,
        IDataStore store, IBlobStore blobs, IResizeQueue queue, ILogger log, CancellationToken ct)
    {
        if (InvalidSize(size) is { } problem) return problem;
        var media = await Find(store, http.User.UserId(), albumId, collection, id, ct);
        if (media is null) return Problems.MediaNotFound();
        return await StreamAsync(http, blobs, store, queue, log, media, size, "private, max-age=31536000, immutable", ct); // a media id's bytes never change
    }

    /// <summary>`?size=` must be one of the sizes photos are made in.</summary>
    internal static IResult? InvalidSize(int? size) => size is { } s && !Renditions.Sizes.Contains(s)
        ? Problems.Validation($"size must be {string.Join(", ", Renditions.Sizes.SkipLast(1))} or {Renditions.Sizes[^1]}.")
        : null;

    /// <summary>Asks the background worker for the photo's smaller versions; false (and logged) if the queue can't be reached.</summary>
    private static async Task<bool> TryQueueResizeAsync(IResizeQueue queue, MediaFile media, ILogger log)
    {
        try
        {
            await queue.EnqueueAsync(new ResizeJob(media.OwnerId, media.AlbumId, media.Id), CancellationToken.None);
            return true;
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Couldn't queue {MediaId} for smaller versions", media.Id);
            return false;
        }
    }

    /// <summary>Queues a photo from before smaller versions existed (ResizeState None) for them.</summary>
    internal static async Task QueueOlderPhotoAsync(IDataStore store, IResizeQueue queue, ILogger log, MediaFile media, CancellationToken ct)
    {
        // Marked first: the worker may finish before a later write, which would then undo its work.
        await store.SetResizeStateAsync(media.OwnerId, media.AlbumId, media.Id, ResizeState.Pending, [], ct);
        if (!await TryQueueResizeAsync(queue, media, log))
            await store.SetResizeStateAsync(media.OwnerId, media.AlbumId, media.Id, ResizeState.None, [], ct);
    }

    /// <summary>
    /// Sends a file's bytes (Range requests supported). With `size` (photos): the smallest of its smaller
    /// versions at least that big, else the original, which is what a photo no bigger than `size` gets
    /// anyway. A photo whose versions aren't made yet gets the original, marked not to be cached for long,
    /// and one from before they existed is queued for them.
    /// </summary>
    internal static async Task<IResult> StreamAsync(HttpContext http, IBlobStore blobs, IDataStore store, IResizeQueue queue, ILogger log,
        MediaFile media, int? size, string cacheControl, CancellationToken ct)
    {
        if (size is not { } wanted || media.Kind != MediaKind.Image) return await StreamAsync(http, blobs, media, cacheControl, ct);

        if (media.ResizeState == ResizeState.None) await QueueOlderPhotoAsync(store, queue, log, media, ct);
        if (media.ResizeState is ResizeState.None or ResizeState.Pending)
            cacheControl = "private, no-cache"; // the smaller version replaces it once made

        if (media.Sizes.Where(s => s >= wanted).Order().FirstOrDefault() is > 0 and var pick
            && await blobs.OpenReadAsync(BlobNames.Sized(media.BlobName, pick), ct) is { } stream)
        {
            var headers = http.Response.Headers;
            headers.CacheControl = cacheControl;
            headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = Path.ChangeExtension(media.FileName, ".jpg") }.ToString();
            return Results.Stream(stream, Renditions.ContentType,
                lastModified: DateTime.SpecifyKind(media.UploadedAt, DateTimeKind.Utc),
                entityTag: new EntityTagHeaderValue($"\"{media.Id:N}-{pick}\""),
                enableRangeProcessing: true);
        }
        return await StreamAsync(http, blobs, media, cacheControl, ct);
    }

    /// <summary>Sends a file's bytes (Range requests supported).</summary>
    internal static async Task<IResult> StreamAsync(HttpContext http, IBlobStore blobs, MediaFile media, string cacheControl, CancellationToken ct)
    {
        var stream = await blobs.OpenReadAsync(media.BlobName, ct);
        if (stream is null) return Problems.MediaNotFound();

        var headers = http.Response.Headers;
        headers.CacheControl = cacheControl;
        headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = media.FileName }.ToString();
        return Results.Stream(stream, media.ContentType,
            lastModified: DateTime.SpecifyKind(media.UploadedAt, DateTimeKind.Utc),
            entityTag: new EntityTagHeaderValue($"\"{media.Id:N}\""),
            enableRangeProcessing: true);
    }

    private static async Task<IResult> GetThumbnail(Guid albumId, Guid id, HttpContext http, IDataStore store, IBlobStore blobs, CancellationToken ct)
    {
        var media = await Find(store, http.User.UserId(), albumId, MediaCollection.Images, id, ct);
        return media is null ? Problems.MediaNotFound()
            : await ThumbnailAsync(http, store, blobs, media, "private, max-age=31536000, immutable", ct); // a photo's preview never changes
    }

    /// <summary>The photo's preview; made now (and kept) if it was uploaded before previews existed.</summary>
    internal static async Task<IResult> ThumbnailAsync(HttpContext http, IDataStore store, IBlobStore blobs, MediaFile media, string cacheControl, CancellationToken ct)
    {
        var noThumbnail = Problems.Of(404, "no_thumbnail", "This file has no preview.");
        if (media.Kind != MediaKind.Image || media.ThumbnailState == ThumbnailState.Unavailable) return noThumbnail;

        var name = BlobNames.Thumbnail(media.BlobName);
        var stream = media.ThumbnailState == ThumbnailState.Ready ? await blobs.OpenReadAsync(name, ct) : null;
        if (stream is null)
        {
            byte[]? bytes;
            await using (var original = await blobs.OpenReadAsync(media.BlobName, ct))
            {
                if (original is null) return Problems.MediaNotFound();
                bytes = Thumbnails.TryCreate(original);
            }
            if (bytes is not null) await blobs.UploadAsync(name, new MemoryStream(bytes), Thumbnails.ContentType, ct);
            var state = bytes is null ? ThumbnailState.Unavailable : ThumbnailState.Ready;
            await store.SetThumbnailStateAsync(media.OwnerId, media.AlbumId, media.Id, state, ct);
            if (bytes is null) return noThumbnail;
            stream = new MemoryStream(bytes);
        }

        http.Response.Headers.CacheControl = cacheControl;
        return Results.Stream(stream, Thumbnails.ContentType, entityTag: new EntityTagHeaderValue($"\"{media.Id:N}-thumb\""));
    }

    private static async Task<IResult> DeleteMany(Guid albumId, DeleteMediaRequest body, MediaCollection collection,
        HttpContext http, IDataStore store, IBlobStore blobs, ILogger log, CancellationToken ct)
    {
        var ids = body.Ids?.Distinct().ToHashSet() ?? [];
        if (ids.Count == 0) return Problems.Validation("Give the ids of the files to delete.");
        if (ids.Count > Limits.DeleteBatch) return Problems.Validation($"At most {Limits.DeleteBatch} files at a time.");

        var ownerId = http.User.UserId();
        if (!await AlbumExists(store, ownerId, albumId, ct)) return Problems.AlbumNotFound();
        var media = (await store.ListMediaAsync(ownerId, albumId, ct)).Where(m => m.Collection == collection && ids.Contains(m.Id)).ToList();

        // Rows first, a few at a time; the album is marked changed once, at the end.
        var deleted = new System.Collections.Concurrent.ConcurrentBag<MediaFile>();
        await Parallel.ForEachAsync(media, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (m, token) =>
        {
            if (await store.DeleteMediaAsync(ownerId, albumId, m.Id, token)) deleted.Add(m);
        });
        if (!deleted.IsEmpty) await store.BumpVersionAsync(ownerId, albumId, DateTime.UtcNow, ct);

        // Then the files. The rows are gone, so a file left behind is unreachable, only wasted space.
        await Parallel.ForEachAsync(deleted, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (m, token) =>
        {
            try
            {
                await blobs.DeleteAsync(m.BlobName, token);
                if (m.Kind == MediaKind.Image)
                    foreach (var made in BlobNames.MadeFrom(m.BlobName)) await blobs.DeleteAsync(made, token);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Media {MediaId} deleted; its blob was left behind", m.Id);
            }
        });
        log.LogInformation("Deleted {Count} files from album {AlbumId}", deleted.Count, albumId);
        return Results.Ok(new DeleteMediaResult(deleted.Count));
    }

    private static async Task<IResult> Delete(Guid albumId, Guid id, MediaCollection collection, HttpContext http, IDataStore store, IBlobStore blobs, ILogger log, CancellationToken ct)
    {
        var ownerId = http.User.UserId();
        var media = await Find(store, ownerId, albumId, collection, id, ct);
        if (media is null) return Problems.MediaNotFound();

        if (!await store.DeleteMediaAsync(ownerId, albumId, id, ct)) return Problems.MediaNotFound(); // deleted concurrently
        await store.BumpVersionAsync(ownerId, albumId, DateTime.UtcNow, ct);

        try
        {
            await blobs.DeleteAsync(media.BlobName, ct);
            if (media.Kind == MediaKind.Image)
                foreach (var made in BlobNames.MadeFrom(media.BlobName)) await blobs.DeleteAsync(made, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Media {MediaId} deleted; its blob was left behind", media.Id);
        }
        return Results.NoContent();
    }

    /// <summary>Keeps only the base name, without control or path characters, at most 255 chars.</summary>
    public static string SafeFileName(string? raw)
    {
        var name = Path.GetFileName((raw ?? "").Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c) && c is not ('/' or '\\' or '"')).ToArray()).Trim();
        if (name.Length > Limits.FileNameLength)
        {
            var ext = Path.GetExtension(name);
            name = name[..(Limits.FileNameLength - ext.Length)] + ext;
        }
        return name.Length == 0 ? "file" : name;
    }

}
