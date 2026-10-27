using System.Text.Json;
using Microsoft.Extensions.Options;
using Slideshow.Api.Data;
using Slideshow.Api.Identity;
using Slideshow.Api.Media;
using Slideshow.Api.Storage;

namespace Slideshow.Api.Endpoints;

public static class AlbumEndpoints
{
    internal static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web);

    public static void MapAlbums(this RouteGroupBuilder api, long maxUploadBytes)
    {
        var g = api.MapGroup("/albums").WithTags("Albums").AddEndpointFilter(RequireRegistered);

        g.MapGet("/", List).WithSummary("The caller's albums, newest first.");
        g.MapPost("/", Create).WithSummary("Create an album.");
        g.MapGet("/{albumId:guid}", Get).WithSummary("The compiled, playable album (manifest).");
        g.MapGet("/{albumId:guid}/info", GetInfo).WithSummary("Album details and counts.");
        g.MapPatch("/{albumId:guid}", Update).WithSummary("Rename an album or change its description.");
        g.MapPost("/{albumId:guid}/compile", Compile).WithSummary("Order the slides by date taken and store the manifest.");
        g.MapDelete("/{albumId:guid}", Delete).WithSummary("Delete the album with its images and music.");
        SharingEndpoints.MapOwner(g);

        MediaEndpoints.MapCollection(g, "images", MediaCollection.Images, maxUploadBytes);
        MediaEndpoints.MapCollection(g, "music", MediaCollection.Music, maxUploadBytes);
    }

    public static async ValueTask<object?> RequireRegistered(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next) =>
        ctx.HttpContext.Account() is null ? Problems.NotRegistered() : await next(ctx);

    private static AlbumInfoDto Info(Album a, IReadOnlyCollection<MediaFile> media)
    {
        var photos = media.Where(m => m.Kind == MediaKind.Image && m.ThumbnailState != ThumbnailState.Unavailable).ToList();
        // Cover: first photo in slideshow order, else the first photo uploaded.
        var cover = photos.Where(m => MediaInfoDto.MissingFor(m).Count == 0)
                        .OrderBy(m => m.TakenAt).ThenBy(m => m.FileName, StringComparer.Ordinal).Select(m => (Guid?)m.Id).FirstOrDefault()
                    ?? photos.OrderBy(m => m.UploadedAt).Select(m => (Guid?)m.Id).FirstOrDefault();
        return new AlbumInfoDto(
            a.Id, a.Name, a.Description, a.CreatedAt, a.UpdatedAt,
            media.Count(m => m.Kind == MediaKind.Image),
            media.Count(m => m.Kind == MediaKind.Video),
            media.Count(m => m.Collection == MediaCollection.Music),
            media.Count(m => MediaInfoDto.MissingFor(m).Count > 0),
            media.Sum(m => m.SizeBytes),
            a.CompiledAt,
            a.CompiledVersion == null || a.CompiledVersion != a.ContentVersion,
            a.ShareToken != null,
            cover);
    }

    private static async Task<IResult> List(HttpContext http, IDataStore store, CancellationToken ct)
    {
        var ownerId = http.User.UserId();
        var albums = await store.ListAlbumsAsync(ownerId, ct);
        var media = (await store.ListMediaAsync(ownerId, null, ct)).ToLookup(m => m.AlbumId);
        return Results.Ok(albums.OrderByDescending(a => a.CreatedAt).Select(a => Info(a, media[a.Id].ToList())));
    }

    private static async Task<IResult> GetInfo(Guid albumId, HttpContext http, IDataStore store, CancellationToken ct)
    {
        var ownerId = http.User.UserId();
        var album = await store.GetAlbumAsync(ownerId, albumId, ct);
        return album is null ? Problems.AlbumNotFound() : Results.Ok(Info(album, await store.ListMediaAsync(ownerId, albumId, ct)));
    }

    /// <summary>The problem with a name or description, or null. A null name means "not given" (allowed only when updating).</summary>
    private static IResult? Invalid(string? name, string? description, bool nameRequired)
    {
        if (name is null ? nameRequired : name.Length == 0) return Problems.Validation("Name is required.");
        if (name?.Length > Limits.AlbumNameLength) return Problems.Validation($"Name can be at most {Limits.AlbumNameLength} characters.");
        if (description?.Length > Limits.AlbumDescriptionLength) return Problems.Validation($"Description can be at most {Limits.AlbumDescriptionLength} characters.");
        return null;
    }

    private static string? CleanDescription(string? d) => string.IsNullOrWhiteSpace(d) ? null : d.Trim();

    private static async Task<IResult> Create(CreateAlbumRequest body, HttpContext http, IDataStore store, IOptions<LimitsOptions> limits, CancellationToken ct)
    {
        var name = body.Name?.Trim();
        var description = CleanDescription(body.Description);
        if (Invalid(name, description, nameRequired: true) is { } problem) return problem;

        var ownerId = http.User.UserId();
        if ((await store.ListAlbumsAsync(ownerId, ct)).Count >= limits.Value.MaxAlbumsPerUser)
            return Problems.Of(409, "album_limit_reached", $"You can have at most {limits.Value.MaxAlbumsPerUser} albums.");

        var now = DateTime.UtcNow;
        var album = new Album { Id = Guid.CreateVersion7(), OwnerId = ownerId, Name = name!, Description = description, CreatedAt = now, UpdatedAt = now };
        await store.AddAlbumAsync(album, ct);
        var info = new AlbumInfoDto(album.Id, album.Name, album.Description, now, now, 0, 0, 0, 0, 0, null, true, false, null);
        return Results.Created($"/api/v1/albums/{album.Id}/info", info);
    }

    private static async Task<IResult> Update(Guid albumId, UpdateAlbumRequest body, HttpContext http, IDataStore store, CancellationToken ct)
    {
        var name = body.Name?.Trim();
        var description = body.Description is null ? null : CleanDescription(body.Description) ?? "";
        if (Invalid(name, description, nameRequired: false) is { } problem) return problem;

        var ownerId = http.User.UserId();
        var album = await store.UpdateAlbumAsync(ownerId, albumId, a =>
        {
            if (name is not null) a.Name = name;
            if (description is not null) a.Description = description.Length == 0 ? null : description;
            a.UpdatedAt = DateTime.UtcNow;
        }, ct);
        return album is null ? Problems.AlbumNotFound() : Results.Ok(Info(album, await store.ListMediaAsync(ownerId, albumId, ct)));
    }

    private static async Task<IResult> Get(Guid albumId, HttpContext http, IDataStore store, IBlobStore blobs, IResizeQueue queue,
        ILogger<Program> log, CancellationToken ct)
    {
        var album = await store.GetAlbumAsync(http.User.UserId(), albumId, ct);
        if (album is null) return Problems.AlbumNotFound();
        var manifest = album.CompiledVersion is null ? null : await ReadManifestAsync(blobs, album, ct);
        if (manifest is null) return Problems.Of(409, "album_not_compiled", "Compile the album first: POST /api/v1/albums/{id}/compile.");
        // The name may have changed since it was compiled (renaming doesn't make the slideshow stale).
        manifest = await WithCurrentSizesAsync(store, queue, log, album, manifest, ct);
        return Results.Ok(manifest with { Name = album.Name, IsStale = album.CompiledVersion != album.ContentVersion });
    }

    /// <summary>
    /// The manifest with each photo's smaller versions as they are now: they're made in the background,
    /// usually after the album was compiled. Slides from before smaller versions existed are queued for
    /// them, so the whole album catches up the first time it's played, not just the photos shown.
    /// </summary>
    internal static async Task<AlbumManifestDto> WithCurrentSizesAsync(IDataStore store, IResizeQueue queue, ILogger log,
        Album album, AlbumManifestDto manifest, CancellationToken ct)
    {
        var media = (await store.ListMediaAsync(album.OwnerId, album.Id, ct)).ToDictionary(m => m.Id);
        var older = manifest.Slides
            .Select(s => media.GetValueOrDefault(s.Id))
            .Where(m => m is { Kind: MediaKind.Image, ResizeState: ResizeState.None })
            .ToList();
        if (older.Count > 0)
        {
            await Parallel.ForEachAsync(older, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
                async (m, token) => await MediaEndpoints.QueueOlderPhotoAsync(store, queue, log, m!, token));
            log.LogInformation("Queued {Count} photos of album {AlbumId} for smaller versions", older.Count, album.Id);
        }

        var sizes = media.ToDictionary(p => p.Key, p => p.Value.Sizes);
        return manifest with
        {
            Slides = manifest.Slides.Select(s => s with { Sizes = s.Kind == "image" ? sizes.GetValueOrDefault(s.Id, []) : [] }).ToList(),
            Music = manifest.Music.Select(m => m with { Sizes = [] }).ToList(), // (null in manifests from before sizes existed)
        };
    }

    /// <summary>The album's stored manifest, or null if it has none.</summary>
    internal static async Task<AlbumManifestDto?> ReadManifestAsync(IBlobStore blobs, Album album, CancellationToken ct)
    {
        await using var stream = await blobs.OpenReadAsync(BlobNames.Manifest(album.OwnerId, album.Id), ct);
        return stream is null ? null : await JsonSerializer.DeserializeAsync<AlbumManifestDto>(stream, ManifestJson, ct);
    }

    private static async Task<IResult> Compile(Guid albumId, HttpContext http, IDataStore store, IBlobStore blobs, CancellationToken ct)
    {
        var album = await store.GetAlbumAsync(http.User.UserId(), albumId, ct);
        if (album is null) return Problems.AlbumNotFound();
        return await CompileAsync(store, blobs, album, ct) is { } manifest ? Results.Ok(manifest) : Problems.AlbumNotFound();
    }

    /// <summary>Orders the album's playable slides and stores the manifest; null if the album was deleted meanwhile.</summary>
    internal static async Task<AlbumManifestDto?> CompileAsync(IDataStore store, IBlobStore blobs, Album album, CancellationToken ct)
    {
        // The version is the one read before the media: anything added meanwhile leaves the album marked stale.
        var version = album.ContentVersion;
        var albumId = album.Id;
        var media = await store.ListMediaAsync(album.OwnerId, albumId, ct);

        // Only photos/videos with a date and a location are shown: the slideshow plays in date order along a route.
        var images = media.Where(m => m.Collection == MediaCollection.Images).ToList();
        var slides = images.Where(m => MediaInfoDto.MissingFor(m).Count == 0)
            .OrderBy(m => m.TakenAt)
            .ThenBy(m => m.FileName, NaturalStringComparer.Instance)
            .ThenBy(m => m.Id)
            .Select(m => MediaInfoDto.From(m, MediaEndpoints.ContentUrl(albumId, "images", m.Id)))
            .ToList();
        var music = media.Where(m => m.Collection == MediaCollection.Music)
            .OrderBy(m => m.FileName, NaturalStringComparer.Instance)
            .ThenBy(m => m.Id)
            .Select(m => MediaInfoDto.From(m, MediaEndpoints.ContentUrl(albumId, "music", m.Id)))
            .ToList();

        var now = DateTime.UtcNow;
        var manifest = new AlbumManifestDto(album.Id, album.Name, now, false, slides, music, images.Count - slides.Count);
        await blobs.UploadAsync(BlobNames.Manifest(album.OwnerId, albumId),
            new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson)), "application/json", ct);
        var updated = await store.UpdateAlbumAsync(album.OwnerId, albumId, a => { a.CompiledAt = now; a.CompiledVersion = version; }, ct);
        return updated is null ? null : manifest;
    }

    private static async Task<IResult> Delete(Guid albumId, HttpContext http, IDataStore store, IBlobStore blobs, ILogger<Program> log, CancellationToken ct)
    {
        var ownerId = http.User.UserId();
        var album = await store.GetAlbumAsync(ownerId, albumId, ct);
        if (album is null) return Problems.AlbumNotFound();

        // Media rows, then the share link, then the album: a retry after a failure picks up where it stopped.
        await store.DeleteAllMediaAsync(ownerId, albumId, ct);
        if (album.ShareToken is { } token) await store.DeleteShareAsync(token, ct);
        if (!await store.DeleteAlbumAsync(ownerId, albumId, ct)) return Problems.AlbumNotFound(); // deleted concurrently

        try { await blobs.DeletePrefixAsync(BlobNames.AlbumPrefix(ownerId, albumId), ct); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Album {AlbumId} deleted; some of its blobs were left behind", albumId); // unreachable, so harmless
        }
        return Results.NoContent();
    }
}
