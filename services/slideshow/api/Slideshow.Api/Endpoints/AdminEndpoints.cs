using Microsoft.Extensions.Options;
using Slideshow.Api.Data;
using Slideshow.Api.Storage;

namespace Slideshow.Api.Endpoints;

/// <summary>
/// The site admins' overview (overview.html): every user and every trip, with their counts, storage and views,
/// and any trip's slideshow, so reported photos can be looked at. Site admins are the users given the API's
/// Entra app role <see cref="SiteAdminRole"/> in the portal. Opening someone's trip is logged. Nothing here
/// changes anyone's data, and nothing is cached.
/// </summary>
public static class AdminEndpoints
{
    public const string SiteAdminPolicy = "SiteAdmin";
    /// <summary>The app role on the API's app registration; Entra lists it in the token's "roles" claim.</summary>
    public const string SiteAdminRole = "Site.Admin";
    private const string NoStore = "private, no-store";

    public static void MapSiteAdmin(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/admin").RequireAuthorization(SiteAdminPolicy).WithTags("Site admin").AddEndpointFilter(NoCaching);
        g.MapGet("/access", () => Results.Ok(new { siteAdmin = true })).WithSummary("200 for site admins, 403 for everyone else.");
        g.MapGet("/overview", Overview).WithSummary("Totals, every user and every trip, with storage used and share-link views.");
        g.MapGet("/trips/{ownerId:guid}/{albumId:guid}", Trip).WithSummary("Any trip's slideshow (logged).");
        g.MapGet("/trips/{ownerId:guid}/{albumId:guid}/images/{id:guid}", TripFile)
            .WithSummary("A photo or video of any trip. Photos: ?size=720, 1280 or 1920 for a smaller version where there is one.");
    }

    private static async ValueTask<object?> NoCaching(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        ctx.HttpContext.Response.Headers.CacheControl = NoStore;
        return await next(ctx);
    }

    private static string TripUrl(string ownerId, Guid albumId) => $"/api/v1/admin/trips/{ownerId}/{albumId}";

    /// <summary>
    /// Reads the accounts, all albums, all media rows and the activity table: one scan of each, which takes a
    /// moment with hundreds of thousands of files. Storage is the files' sizes as uploaded (as on the account page).
    /// </summary>
    private static async Task<IResult> Overview(ISlideshowStore store, IUserStore userStore, IOptions<LimitsOptions> limits, CancellationToken ct)
    {
        var usersTask = userStore.ListUsersAsync(ct);
        var albumsTask = store.ListAllAlbumsAsync(ct);
        var mediaTask = store.ListMediaAcrossOwnersAsync(MediaCollection.Images, null, ct);
        var activityTask = store.ReadActivityAsync(ct);
        await Task.WhenAll(usersTask, albumsTask, mediaTask, activityTask);
        var accounts = usersTask.Result.ToDictionary(u => u.Id);
        var albums = albumsTask.Result;
        var media = mediaTask.Result;
        var activity = activityTask.Result;
        var now = DateTime.UtcNow;

        var mediaByAlbum = media.ToLookup(m => (m.OwnerId, m.AlbumId));
        var trips = albums.Select(a =>
        {
            var files = mediaByAlbum[(a.OwnerId, a.Id)].ToList();
            var owner = accounts.GetValueOrDefault(a.OwnerId);
            var views = activity.Views.GetValueOrDefault((a.OwnerId, a.Id));
            return new OverviewTripDto(a.Id, a.OwnerId, owner?.DisplayName, owner?.Email, a.Name, a.Description, a.CreatedAt, a.UpdatedAt,
                files.Count(m => m.Kind == MediaKind.Image), files.Count(m => m.Kind == MediaKind.Video),
                files.Count(m => MediaInfoDto.MissingFor(m).Count > 0), files.Sum(m => m.SizeBytes),
                a.ShareToken is not null, a.IsPublic && a.ShareToken is not null, views?.Count ?? 0, views?.LastViewedAt);
        }).OrderByDescending(t => t.UpdatedAt).ToList();

        // Everyone with an account, and (in case one is missing) anyone who owns trips or files.
        var mediaByOwner = media.ToLookup(m => m.OwnerId);
        var tripsByOwner = albums.ToLookup(a => a.OwnerId);
        var userIds = accounts.Keys.Concat(albums.Select(a => a.OwnerId)).Concat(media.Select(m => m.OwnerId)).Distinct();
        var users = userIds.Select(id =>
        {
            var account = accounts.GetValueOrDefault(id);
            var files = mediaByOwner[id].ToList();
            return new OverviewUserDto(id, account?.DisplayName, account?.Email, account?.CreatedAt,
                activity.LastSeen.TryGetValue(id, out var seen) ? seen : null,
                tripsByOwner[id].Count(), files.Count(m => m.Kind == MediaKind.Image), files.Count(m => m.Kind == MediaKind.Video),
                files.Sum(m => m.SizeBytes));
        }).OrderByDescending(u => u.LastSeenAt ?? DateTime.MinValue).ThenByDescending(u => u.CreatedAt ?? DateTime.MinValue).ToList();

        var monthAgo = now.AddDays(-30);
        var totals = new OverviewTotalsDto(
            Users: users.Count,
            NewUsers30Days: users.Count(u => u.CreatedAt >= monthAgo),
            ActiveUsers30Days: users.Count(u => u.LastSeenAt >= monthAgo),
            Trips: trips.Count,
            SharedTrips: trips.Count(t => t.IsShared),
            PublicTrips: trips.Count(t => t.IsPublic),
            Photos: media.Count(m => m.Kind == MediaKind.Image),
            Videos: media.Count(m => m.Kind == MediaKind.Video),
            Bytes: media.Sum(m => m.SizeBytes),
            Views: trips.Sum(t => t.Views),
            ResizeFailures: media.Count(m => m.Kind == MediaKind.Image && m.ResizeState == ResizeState.Unavailable));
        return Results.Ok(new OverviewDto(now, limits.Value.QuotaBytesPerUser, totals, users, trips));
    }

    /// <summary>
    /// The trip's slideshow as its viewers would see it: built on the spot, so the owner's trip isn't touched
    /// (not even compiled). Photos without a date or location aren't in it, as for everyone else.
    /// </summary>
    private static async Task<IResult> Trip(Guid ownerId, Guid albumId, HttpContext http, ISlideshowStore store, IUserStore userStore,
        MusicLibrary library, ILogger<SlideshowLog> log, CancellationToken ct)
    {
        var owner = ownerId.ToString();
        var album = await store.GetAlbumAsync(owner, albumId, ct);
        if (album is null) return SlideshowProblems.AlbumNotFound();
        var images = (await store.ListMediaAsync(owner, albumId, ct)).Where(m => m.Collection == MediaCollection.Images).ToList();
        var slides = AlbumEndpoints.PlayOrder(images)
            .Select(m => MediaInfoDto.From(m, $"{TripUrl(owner, albumId)}/images/{m.Id}") with { ThumbnailUrl = null })
            .ToList();
        var account = await userStore.GetUserAsync(owner, ct);

        log.LogInformation("Site admin {AdminId} opened trip {AlbumId} of user {OwnerId}", http.User.UserId(), albumId, owner);
        return Results.Ok(new AdminTripDto(album.Id, album.Name, owner, account?.DisplayName, account?.Email,
            slides, await library.TracksForAsync(album, ct), images.Count - slides.Count));
    }

    private static async Task<IResult> TripFile(Guid ownerId, Guid albumId, Guid id, int? size, HttpContext http, ISlideshowStore store,
        IBlobStore blobs, IResizeQueue queue, ILogger<SlideshowLog> log, CancellationToken ct)
    {
        if (MediaEndpoints.InvalidSize(size) is { } problem) return problem;
        var media = await store.GetMediaAsync(ownerId.ToString(), albumId, id, ct);
        if (media is not { Collection: MediaCollection.Images }) return SlideshowProblems.MediaNotFound();
        return await MediaEndpoints.StreamAsync(http, blobs, store, queue, log, media, size, NoStore, ct);
    }
}
