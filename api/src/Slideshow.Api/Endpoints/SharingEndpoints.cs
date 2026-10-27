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

    private static ShareDto ToDto(Album a) => new(a.ShareToken is not null, a.ShareToken, !a.ShareHideMap);


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
    /// Points the album at a new share code (or none), then retires the old code. The new code is
    /// recorded first, so a live link never points nowhere; if the album has gone meanwhile it is released.
    /// </summary>
    private static async Task<Album?> SetTokenAsync(IDataStore store, string ownerId, Guid albumId, bool share, bool? showMap, CancellationToken ct)
    {
        var newToken = share ? await ClaimTokenAsync(store, ownerId, albumId, ct) : null;
        string? oldToken = null;
        var album = await store.UpdateAlbumAsync(ownerId, albumId, a =>
        {
            oldToken = a.ShareToken;
            a.ShareToken = newToken;
            if (showMap is { } show) a.ShareHideMap = !show;
        }, ct);
        var retired = album is null ? newToken : oldToken;
        if (retired is not null) await store.DeleteShareAsync(retired, ct);
        return album;
    }

    private static async Task<IResult> Update(Guid albumId, UpdateShareRequest body, HttpContext http, IDataStore store, ILogger<Program> log, CancellationToken ct)
    {
        var ownerId = http.User.UserId();
        var album = await store.GetAlbumAsync(ownerId, albumId, ct);
        if (album is null) return Problems.AlbumNotFound();

        var shared = album.ShareToken is not null;
        if (body.Enabled == true && !shared)        // turning on again gives a new link
            album = await SetTokenAsync(store, ownerId, albumId, true, body.ShowMap, ct);
        else if (body.Enabled == false && shared)   // old links stop working
            album = await SetTokenAsync(store, ownerId, albumId, false, body.ShowMap, ct);
        else if (body.ShowMap is { } showMap)
            album = await store.UpdateAlbumAsync(ownerId, albumId, a => a.ShareHideMap = !showMap, ct);
        if (album is null) return Problems.AlbumNotFound();

        log.LogInformation("Album {AlbumId} sharing: {Shared}", albumId, album.ShareToken is not null);
        return Results.Ok(ToDto(album));
    }

    private static async Task<IResult> Reset(Guid albumId, HttpContext http, IDataStore store, CancellationToken ct)
    {
        var ownerId = http.User.UserId();
        var album = await store.GetAlbumAsync(ownerId, albumId, ct);
        if (album is null) return Problems.AlbumNotFound();
        if (album.ShareToken is null) return Problems.Of(409, "album_not_shared", "Turn on link sharing first.");
        album = await SetTokenAsync(store, ownerId, albumId, true, null, ct);
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

    private static async Task<IResult> GetSharedFile(string token, Guid id, int? size, MediaCollection collection, HttpContext http,
        IDataStore store, IBlobStore blobs, IResizeQueue queue, ILogger log, CancellationToken ct)
    {
        if (MediaEndpoints.InvalidSize(size) is { } problem) return problem;
        var album = await SharedAlbum(store, token, ct);
        if (album is null) return NotShared();
        var media = await store.GetMediaAsync(album.OwnerId, album.Id, id, ct);
        if (media?.Collection != collection) media = null;
        // Only what the slideshow shows: photos/videos left out of it (no date or location) stay private.
        if (media is null || MediaInfoDto.MissingFor(media).Count > 0) return Problems.MediaNotFound();
        return await MediaEndpoints.StreamAsync(http, blobs, store, queue, log, media, size, "private, max-age=3600", ct);
    }
}
