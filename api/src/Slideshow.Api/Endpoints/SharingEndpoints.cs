using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Slideshow.Api.Data;
using Slideshow.Api.Identity;
using Slideshow.Api.Storage;

namespace Slideshow.Api.Endpoints;

/// <summary>
/// Link sharing: the owner turns on "anyone with the link can watch" for an album, which gives it a
/// secret, unguessable code (128 random bits). Anyone with the code can play the album's slideshow
/// without signing in — and nothing else: no file list, no other albums, no changes. Turning sharing
/// off (or getting a new link) makes old links stop working at once.
/// A shared album can also be made public: then it's listed, with its link, on the Albums page for everyone.
/// </summary>
public static partial class SharingEndpoints
{
    // ───────── Owner: /api/v1/albums/{albumId}/share ─────────

    public static void MapOwner(RouteGroupBuilder albums)
    {
        var g = albums.MapGroup("/{albumId:guid}/share").WithTags("Sharing");
        g.MapGet("/", Get).WithSummary("Whether the album is shared by link, and its share code.");
        g.MapPut("/", Update).WithSummary("Turn link sharing on/off; choose whether viewers see the map.");
        g.MapPost("/reset", Reset).WithSummary("Replace the share link: the old one stops working.");
    }

    private static ShareDto ToDto(Album a) => new(a.ShareToken is not null, a.ShareToken, !a.ShareHideMap, a.IsPublic);


    /// <summary>128 random bits, URL-safe (22 characters).</summary>
    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<IResult> Get(Guid albumId, HttpContext http, IDataStore store, CancellationToken ct) =>
        await store.GetAlbumAsync(http.User.UserId(), albumId, ct) is { } album ? Results.Ok(ToDto(album)) : Problems.AlbumNotFound();

    /// <summary>A new code, recorded in the share lookup for the album (codes are random, so a clash is near-impossible).</summary>
    private static async Task<string> ClaimTokenAsync(IDataStore store, string ownerId, Guid albumId, CancellationToken ct)
    {
        while (true)
        {
            var token = NewToken();
            if (await store.TryAddShareAsync(token, ownerId, albumId, ct)) return token;
        }
    }

    /// <summary>
    /// Points the album at a new share code (or none), also applying `change`, then retires the old code. The
    /// new code is recorded first, so a live link never points nowhere; if the album has gone meanwhile it is
    /// released. An album that isn't shared is never public.
    /// </summary>
    private static async Task<Album?> SetTokenAsync(IDataStore store, string ownerId, Guid albumId, bool share, Action<Album> change, CancellationToken ct)
    {
        var newToken = share ? await ClaimTokenAsync(store, ownerId, albumId, ct) : null;
        string? oldToken = null;
        var album = await store.UpdateAlbumAsync(ownerId, albumId, a =>
        {
            oldToken = a.ShareToken;
            a.ShareToken = newToken;
            change(a);
            if (newToken is null) a.IsPublic = false;
        }, ct);
        var retired = album is null ? newToken : oldToken;
        if (retired is not null) await store.DeleteShareAsync(retired, ct);
        return album;
    }

    private static async Task<IResult> Update(Guid albumId, UpdateShareRequest body, HttpContext http, IDataStore store, ILogger<Program> log, CancellationToken ct)
    {
        if (body.Enabled == false && body.IsPublic == true) return Problems.Validation("A public album has to be shared by link.");
        var ownerId = http.User.UserId();
        var album = await store.GetAlbumAsync(ownerId, albumId, ct);
        if (album is null) return Problems.AlbumNotFound();

        void Apply(Album a)
        {
            if (body.ShowMap is { } show) a.ShareHideMap = !show;
            if (body.IsPublic is { } isPublic) a.IsPublic = isPublic && a.ShareToken is not null;
        }
        var shared = album.ShareToken is not null;
        var enable = body.Enabled ?? (body.IsPublic == true ? true : null); // making it public shares it
        if (enable == true && !shared)        // turning on again gives a new link
            album = await SetTokenAsync(store, ownerId, albumId, true, Apply, ct);
        else if (enable == false && shared)   // old links stop working, and it's no longer public
            album = await SetTokenAsync(store, ownerId, albumId, false, Apply, ct);
        else if (body.ShowMap is not null || body.IsPublic is not null)
            album = await store.UpdateAlbumAsync(ownerId, albumId, Apply, ct);
        if (album is null) return Problems.AlbumNotFound();

        log.LogInformation("Album {AlbumId} sharing: {Shared}, public: {Public}", albumId, album.ShareToken is not null, album.IsPublic);
        return Results.Ok(ToDto(album));
    }

    private static async Task<IResult> Reset(Guid albumId, HttpContext http, IDataStore store, CancellationToken ct)
    {
        var ownerId = http.User.UserId();
        var album = await store.GetAlbumAsync(ownerId, albumId, ct);
        if (album is null) return Problems.AlbumNotFound();
        if (album.ShareToken is null) return Problems.Of(409, "album_not_shared", "Turn on link sharing first.");
        album = await SetTokenAsync(store, ownerId, albumId, true, _ => { }, ct); // a public album stays public, under its new link
        return album is null ? Problems.AlbumNotFound() : Results.Ok(ToDto(album));
    }

    // ───────── Anyone with the link: /api/v1/shared/{token} ─────────

    public static void MapShared(this RouteGroupBuilder shared)
    {
        shared.WithTags("Shared");
        shared.MapGet("/{token}", GetShared).WithSummary("The shared album's slideshow (no sign-in).");
        shared.MapGet("/{token}/images/{id:guid}", (string token, Guid id, int? size, HttpContext http, IDataStore store, IBlobStore blobs, IResizeQueue queue, ILogger<Program> log, CancellationToken ct) =>
                GetSharedFile(token, id, size, MediaCollection.Images, http, store, blobs, queue, log, ct))
            .WithSummary("A photo or video of a shared album. Photos: ?size=720, 1280 or 1920 for a smaller version where there is one.");
        shared.MapGet("/{token}/music/{id:guid}", (string token, Guid id, HttpContext http, IDataStore store, IBlobStore blobs, IResizeQueue queue, ILogger<Program> log, CancellationToken ct) =>
            GetSharedFile(token, id, null, MediaCollection.Music, http, store, blobs, queue, log, ct)).WithSummary("A music track of a shared album.");
        shared.MapGet("/{token}/images/{id:guid}/thumbnail", GetSharedThumbnail).WithSummary("A small JPEG preview of a photo in a shared album's slideshow.");
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{22}$")]
    private static partial Regex TokenFormat();

    /// <summary>A wrong, revoked or malformed code all look the same: not found.</summary>
    private static IResult NotShared() => Problems.Of(404, "share_not_found", "This slideshow isn't shared, or the link is wrong.");

    /// <summary>The album the code points to, if the album still has that code (a turned-off link finds nothing).</summary>
    private static async Task<Album?> SharedAlbum(IDataStore store, string token, CancellationToken ct)
    {
        if (!TokenFormat().IsMatch(token) || await store.ResolveShareAsync(token, ct) is not { } share) return null;
        var album = await store.GetAlbumAsync(share.OwnerId, share.AlbumId, ct);
        return album?.ShareToken == token ? album : null;
    }

    private static async Task<IResult> GetShared(string token, IDataStore store, IBlobStore blobs, IResizeQueue queue, ILogger<Program> log, CancellationToken ct)
    {
        var album = await SharedAlbum(store, token, ct);
        if (album is null) return NotShared();

        // Viewers always get the current slideshow: compile if the owner changed the album since.
        var manifest = album.CompiledVersion == album.ContentVersion ? await AlbumEndpoints.ReadManifestAsync(blobs, album, ct) : null;
        manifest ??= await AlbumEndpoints.CompileAsync(store, blobs, album, ct);
        if (manifest is null) return NotShared(); // deleted meanwhile
        manifest = await AlbumEndpoints.WithCurrentSizesAsync(store, queue, log, album, manifest, ct);

        var showMap = !album.ShareHideMap;
        MediaInfoDto Shared(MediaInfoDto m, string segment) => m with
        {
            Url = $"/api/v1/shared/{token}/{segment}/{m.Id}",
            ThumbnailUrl = null,
            Location = showMap ? m.Location : null, // hidden map: no locations either
        };
        return Results.Ok(new SharedAlbumDto(album.Name, showMap,
            manifest.Slides.Select(m => Shared(m, "images")).ToList(),
            manifest.Music.Select(m => Shared(m, "music")).ToList()));
    }

    /// <summary>A photo of the album's slideshow, or null: those left out of it (no date or location) stay private.</summary>
    private static async Task<MediaFile?> SlideshowFile(IDataStore store, Album album, Guid id, MediaCollection collection, CancellationToken ct)
    {
        var media = await store.GetMediaAsync(album.OwnerId, album.Id, id, ct);
        return media?.Collection == collection && MediaInfoDto.MissingFor(media).Count == 0 ? media : null;
    }

    private static async Task<IResult> GetSharedThumbnail(string token, Guid id, HttpContext http, IDataStore store, IBlobStore blobs, CancellationToken ct)
    {
        var album = await SharedAlbum(store, token, ct);
        if (album is null) return NotShared();
        var media = await SlideshowFile(store, album, id, MediaCollection.Images, ct);
        return media is null ? Problems.MediaNotFound() : await MediaEndpoints.ThumbnailAsync(http, store, blobs, media, "private, max-age=3600", ct);
    }

    private static async Task<IResult> GetSharedFile(string token, Guid id, int? size, MediaCollection collection, HttpContext http,
        IDataStore store, IBlobStore blobs, IResizeQueue queue, ILogger log, CancellationToken ct)
    {
        if (MediaEndpoints.InvalidSize(size) is { } problem) return problem;
        var album = await SharedAlbum(store, token, ct);
        if (album is null) return NotShared();
        var media = await SlideshowFile(store, album, id, collection, ct);
        if (media is null) return Problems.MediaNotFound();
        return await MediaEndpoints.StreamAsync(http, blobs, store, queue, log, media, size, "private, max-age=3600", ct);
    }

    // ───────── Anyone: /api/v1/public-albums ─────────

    public static void MapPublicAlbums(this RouteGroupBuilder g)
    {
        g.WithTags("Shared");
        g.MapGet("/", ListPublic).WithSummary("Albums their owners made public, newest first, with the share code to play each (no sign-in).");
    }

    private static async Task<IResult> ListPublic(IDataStore store, CancellationToken ct)
    {
        var albums = (await store.ListPublicAlbumsAsync(ct)).Where(a => a.ShareToken is not null).OrderByDescending(a => a.CreatedAt).ToList();
        var media = await Task.WhenAll(albums.Select(a => store.ListMediaAsync(a.OwnerId, a.Id, ct)));
        return Results.Ok(albums.Select((a, i) => PublicInfo(a, media[i])));
    }

    /// <summary>Only what the slideshow shows is counted (or used as the cover): the rest of the album stays private.</summary>
    private static PublicAlbumDto PublicInfo(Album a, IReadOnlyCollection<MediaFile> media)
    {
        var token = a.ShareToken!;
        var slides = media.Where(m => m.Collection == MediaCollection.Images && MediaInfoDto.MissingFor(m).Count == 0).ToList();
        var cover = slides.Where(m => m.Kind == MediaKind.Image && m.ThumbnailState != ThumbnailState.Unavailable)
            .OrderBy(m => m.TakenAt).ThenBy(m => m.FileName, StringComparer.Ordinal).Select(m => (Guid?)m.Id).FirstOrDefault();
        return new PublicAlbumDto(
            a.Id, a.Name, a.Description, a.CreatedAt, a.UpdatedAt,
            slides.Count(m => m.Kind == MediaKind.Image),
            slides.Count(m => m.Kind == MediaKind.Video),
            media.Count(m => m.Collection == MediaCollection.Music),
            token,
            cover is { } c ? $"/api/v1/shared/{token}/images/{c}/thumbnail" : null);
    }
}
