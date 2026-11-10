using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Slideshow.Api.Data;
using Slideshow.Api.Media;
using Slideshow.Api.Storage;

namespace Slideshow.Api.Endpoints;

/// <summary>
/// The music library. Trips choose their music from it (users can't upload music: its licenses have to be
/// checked). Anyone can list and play its tracks, since shared slideshows play them without sign-in. Music
/// admins (the Entra app role <see cref="AdminRole"/>, given in the portal) add, change and delete tracks.
/// </summary>
public static class MusicEndpoints
{
    public const string AdminPolicy = "MusicAdmin";
    /// <summary>The app role on the API's app registration; Entra lists it in the token's "roles" claim.</summary>
    public const string AdminRole = "Music.Admin";
    private const long FormOverhead = 256 * 1024; // the license file, the text fields and multipart framing

    // ───────── Anyone: /api/v1/music ─────────

    public static void MapMusic(this RouteGroupBuilder g)
    {
        g.WithTags("Music");
        g.MapGet("/", async (MusicLibrary library, CancellationToken ct) =>
                Results.Ok((await library.GetAsync(ct)).Values.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).Select(TrackDto.From)))
            .WithSummary("The music library, by title (no sign-in).");
        g.MapGet("/{id:guid}", PlayTrack).WithSummary("A track's MP3 (supports Range; no sign-in).");
        g.MapGet("/{id:guid}/license", License).WithSummary("A track's license certificate, as text (no sign-in).");
    }

    private static async Task<IResult> PlayTrack(Guid id, HttpContext http, MusicLibrary library, IBlobStore blobs, CancellationToken ct)
    {
        if (!(await library.GetAsync(ct)).TryGetValue(id, out var track)) return SlideshowProblems.TrackNotFound();
        var stream = await blobs.OpenReadAsync(BlobNames.Track(track.Id, track.FileVersion), ct);
        if (stream is null) return SlideshowProblems.TrackNotFound();
        var headers = http.Response.Headers;
        headers.CacheControl = "public, max-age=31536000, immutable"; // the URL names the file version
        headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = track.Title + ".mp3" }.ToString();
        return Results.Stream(stream, "audio/mpeg", lastModified: DateTime.SpecifyKind(track.UpdatedAt, DateTimeKind.Utc),
            entityTag: new EntityTagHeaderValue($"\"{track.Id:N}-{track.FileVersion}\""), enableRangeProcessing: true);
    }

    private static async Task<IResult> License(Guid id, HttpContext http, MusicLibrary library, IBlobStore blobs, CancellationToken ct)
    {
        if (!(await library.GetAsync(ct)).ContainsKey(id)) return SlideshowProblems.TrackNotFound();
        var stream = await blobs.OpenReadAsync(BlobNames.TrackLicense(id), ct);
        if (stream is null) return SlideshowProblems.TrackNotFound();
        http.Response.Headers.CacheControl = "public, max-age=3600";
        return Results.Stream(stream, "text/plain; charset=utf-8");
    }

    // ───────── A trip's music: /api/v1/albums/{albumId}/music ─────────

    public static void MapAlbumMusic(RouteGroupBuilder albums)
    {
        var g = albums.MapGroup("/{albumId:guid}/music").WithTags("Music");
        g.MapGet("/", async (Guid albumId, HttpContext http, ISlideshowStore store, MusicLibrary library, CancellationToken ct) =>
                await store.GetAlbumAsync(http.User.UserId(), albumId, ct) is { } album
                    ? Results.Ok(await library.TracksForAsync(album, ct))
                    : SlideshowProblems.AlbumNotFound())
            .WithSummary("The trip's music: library tracks in play order.");
        g.MapPut("/", SetAlbumMusic).WithSummary($"Choose the trip's music: library track ids in play order (at most {Limits.TracksPerTrip}).");
    }

    private static async Task<IResult> SetAlbumMusic(Guid albumId, AlbumMusicRequest body, HttpContext http, ISlideshowStore store, MusicLibrary library, CancellationToken ct)
    {
        var ids = body.TrackIds ?? [];
        if (ids.Count > Limits.TracksPerTrip) return Problems.Validation($"A trip can have at most {Limits.TracksPerTrip} tracks.");
        if (ids.Distinct().Count() != ids.Count) return Problems.Validation("Each track can be chosen only once.");
        var tracks = await library.GetAsync(ct);
        if (ids.Any(id => !tracks.ContainsKey(id))) return Problems.Validation("One of the tracks isn't in the music library any more. Reload the page.");

        var album = await store.UpdateAlbumAsync(http.User.UserId(), albumId, a => { a.MusicTrackIds = [.. ids]; a.UpdatedAt = DateTime.UtcNow; }, ct);
        return album is null ? SlideshowProblems.AlbumNotFound() : Results.Ok(MusicLibrary.TracksFor(album, tracks));
    }

    // ───────── Music admins: /api/v1/admin/music ─────────

    public static void MapMusicAdmin(this RouteGroupBuilder api, long maxMusicBytes)
    {
        var g = api.MapGroup("/admin/music").RequireAuthorization(AdminPolicy).WithTags("Music admin");
        g.MapGet("/access", () => Results.Ok(new { musicAdmin = true })).WithSummary("200 for music admins, 403 for everyone else.");
        g.MapGet("/", AdminList).WithSummary("Every library track, with its files and how many trips use it.");
        g.MapGet("/export", Export).WithSummary("The whole catalog as JSON, with each license's text (a backup; the MP3s aren't in it).");
        g.MapPost("/", (HttpContext http, ISlideshowStore store, IBlobStore blobs, MusicLibrary library, IOptions<LimitsOptions> limits, ILogger<SlideshowLog> log, CancellationToken ct) =>
                Save(null, http, store, blobs, library, limits.Value, log, ct))
            .WithSummary("Add a track (multipart/form-data: file, license, title, artist, licenseName, durationSeconds; optional artistUrl, sourceUrl, tags).")
            .DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(maxMusicBytes + FormOverhead))
            .WithFormOptions(multipartBodyLengthLimit: maxMusicBytes + FormOverhead);
        g.MapPost("/{id:guid}", (Guid id, HttpContext http, ISlideshowStore store, IBlobStore blobs, MusicLibrary library, IOptions<LimitsOptions> limits, ILogger<SlideshowLog> log, CancellationToken ct) =>
                Save(id, http, store, blobs, library, limits.Value, log, ct))
            .WithSummary("Change a track: the same fields, each optional; a new file or license replaces the old one.")
            .DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(maxMusicBytes + FormOverhead))
            .WithFormOptions(multipartBodyLengthLimit: maxMusicBytes + FormOverhead);
        g.MapDelete("/{id:guid}", Delete).WithSummary("Delete a track: its files go, and trips that used it play without it.");
    }

    /// <summary>How many trips use each track.</summary>
    private static async Task<Dictionary<Guid, int>> UsageAsync(ISlideshowStore store, CancellationToken ct) =>
        (await store.ListAlbumsWithMusicAsync(ct)).SelectMany(a => a.MusicTrackIds.Distinct()).GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

    private static async Task<IResult> AdminList(ISlideshowStore store, CancellationToken ct)
    {
        var usage = await UsageAsync(store, ct);
        return Results.Ok((await store.ListTracksAsync(ct)).OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => AdminTrackDto.From(t, usage.GetValueOrDefault(t.Id))));
    }

    private sealed record ExportedTrack(AdminTrackDto Track, string? LicenseText);

    private static async Task<IResult> Export(HttpContext http, ISlideshowStore store, IBlobStore blobs, CancellationToken ct)
    {
        var usage = await UsageAsync(store, ct);
        var tracks = (await store.ListTracksAsync(ct)).OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ToList();
        var exported = new List<ExportedTrack>(tracks.Count);
        foreach (var t in tracks)
        {
            string? text = null;
            await using (var s = await blobs.OpenReadAsync(BlobNames.TrackLicense(t.Id), ct))
                if (s is not null) text = await new StreamReader(s, Encoding.UTF8).ReadToEndAsync(ct);
            exported.Add(new ExportedTrack(AdminTrackDto.From(t, usage.GetValueOrDefault(t.Id)), text));
        }
        http.Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = $"music-library-{DateTime.UtcNow:yyyy-MM-dd}.json" }.ToString();
        return Results.Ok(new { exportedAt = DateTime.UtcNow, tracks = exported });
    }

    /// <summary>Adds (id null) or changes a track from the admin page's form.</summary>
    private static async Task<IResult> Save(Guid? id, HttpContext http, ISlideshowStore store, IBlobStore blobs, MusicLibrary library,
        LimitsOptions limits, ILogger log, CancellationToken ct)
    {
        if (!http.Request.HasFormContentType) return Problems.Validation("Send the track as multipart/form-data.");
        var form = await http.Request.ReadFormAsync(ct);
        var existing = id is { } tid ? await store.GetTrackAsync(tid, ct) : null;
        if (id is not null && existing is null) return SlideshowProblems.TrackNotFound();
        var adding = existing is null;

        string? Text(string name) => form.TryGetValue(name, out var v) ? v.ToString().Trim() : null;
        var title = Text("title");
        var artist = Text("artist");
        var licenseName = Text("licenseName");
        var artistUrl = Text("artistUrl");
        var sourceUrl = Text("sourceUrl");
        var tagsText = Text("tags");
        var durationText = Text("durationSeconds");
        var file = form.Files.GetFile("file");
        var licenseFile = form.Files.GetFile("license");

        // Required to add; when changing, what's left out stays as it is.
        if (adding && (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(artist)))
            return Problems.Validation("Give the track's title and artist.");
        if (adding && string.IsNullOrEmpty(licenseName)) return Problems.Validation("Give the license's name, e.g. Pixabay Content License.");
        if (adding && file is null) return Problems.Validation("Choose the track's MP3.");
        if (adding && licenseFile is null) return Problems.Validation("Choose the track's license certificate (a .txt file).");
        if (id is not null && (title == "" || artist == "" || licenseName == "")) return Problems.Validation("Title, artist and license can't be empty.");
        foreach (var (name, value) in new[] { ("Title", title), ("Artist", artist), ("License name", licenseName) })
            if (value?.Length > Limits.TrackTextLength) return Problems.Validation($"{name} can be at most {Limits.TrackTextLength} characters.");
        foreach (var (name, value) in new[] { ("Artist link", artistUrl), ("Source link", sourceUrl) })
            if (!string.IsNullOrEmpty(value) && (value.Length > Limits.UrlLength || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
                return Problems.Validation($"{name} must be a web address (https://…).");

        IReadOnlyList<string>? tags = null;
        if (tagsText is not null)
        {
            tags = tagsText.Split(',').Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().ToList();
            if (tags.Count > Limits.TagsPerTrack) return Problems.Validation($"At most {Limits.TagsPerTrack} tags.");
            if (tags.Any(t => t.Length > Limits.TagLength)) return Problems.Validation($"A tag can be at most {Limits.TagLength} characters.");
        }

        double? duration = null;
        if (!string.IsNullOrEmpty(durationText))
        {
            if (!double.TryParse(durationText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) || d is <= 0 or > 3 * 3600)
                return Problems.Validation("durationSeconds must be the track's length in seconds.");
            duration = Math.Round(d, 2);
        }
        if (file is not null && duration is null) return Problems.Validation("Give the new MP3's length (durationSeconds).");

        // The files: an MP3, and a license certificate that is text.
        if (file is not null)
        {
            if (file.Length > limits.MaxMusicBytes) return Problems.Of(413, "file_too_large", $"A track can be at most {limits.MaxMusicBytes / (1024 * 1024)} MB.");
            await using var head = file.OpenReadStream();
            var header = new byte[MediaSniffer.HeaderLength];
            var n = await head.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
            if (MediaSniffer.Sniff(header.AsSpan(0, n)).Result is not { Kind: MediaKind.Audio })
                return Problems.Of(415, "unsupported_media_type", "The track must be an MP3.");
        }
        byte[]? licenseBytes = null;
        if (licenseFile is not null)
        {
            if (licenseFile.Length is 0 or > Limits.LicenseBytes) return Problems.Validation($"The license certificate must be a text file of at most {Limits.LicenseBytes / 1024} KB.");
            using var buffer = new MemoryStream();
            await licenseFile.CopyToAsync(buffer, ct);
            licenseBytes = buffer.ToArray();
            try { _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(licenseBytes); }
            catch (DecoderFallbackException) { return Problems.Validation("The license certificate must be a text file."); }
        }

        var now = DateTime.UtcNow;
        var track = existing ?? new MusicTrack
        {
            Id = Guid.CreateVersion7(), Title = "", Artist = "", LicenseName = "", LicenseFileName = "", FileVersion = 0,
            AddedAt = now, AddedBy = http.User.UserId(),
        };
        if (title is not null) track.Title = title;
        if (artist is not null) track.Artist = artist;
        if (licenseName is not null) track.LicenseName = licenseName;
        if (artistUrl is not null) track.ArtistUrl = artistUrl.Length == 0 ? null : artistUrl;
        if (sourceUrl is not null) track.SourceUrl = sourceUrl.Length == 0 ? null : sourceUrl;
        if (tags is not null) track.Tags = tags;
        if (duration is { } secs) track.DurationSeconds = secs; // a new MP3's length, or a correction
        track.UpdatedAt = now;

        var oldMp3 = existing is null ? null : BlobNames.Track(existing.Id, existing.FileVersion);
        string? newMp3 = null;
        if (file is not null)
        {
            track.FileVersion++;
            track.SizeBytes = file.Length;
            newMp3 = BlobNames.Track(track.Id, track.FileVersion);
            await using var content = file.OpenReadStream();
            await blobs.UploadAsync(newMp3, content, "audio/mpeg", ct);
        }
        try
        {
            if (licenseBytes is not null)
            {
                await blobs.UploadAsync(BlobNames.TrackLicense(track.Id), new MemoryStream(licenseBytes), "text/plain; charset=utf-8", ct);
                track.LicenseFileName = MediaEndpoints.SafeFileName(licenseFile!.FileName);
            }
            await store.SaveTrackAsync(track, ct);
        }
        catch
        {
            if (adding) await blobs.DeletePrefixAsync(BlobNames.TrackPrefix(track.Id), CancellationToken.None);
            else if (newMp3 is not null) await blobs.DeleteAsync(newMp3, CancellationToken.None);
            throw;
        }
        library.Invalidate();
        if (newMp3 is not null && oldMp3 is not null && !adding) await blobs.DeleteAsync(oldMp3, CancellationToken.None);

        log.LogInformation("Music library: {Action} track {TrackId} \"{Title}\"", adding ? "added" : "changed", track.Id, track.Title);
        var dto = AdminTrackDto.From(track, adding ? 0 : (await UsageAsync(store, ct)).GetValueOrDefault(track.Id));
        return adding ? Results.Created($"/api/v1/music/{track.Id}", dto) : Results.Ok(dto);
    }

    private static async Task<IResult> Delete(Guid id, ISlideshowStore store, IBlobStore blobs, MusicLibrary library, ILogger<SlideshowLog> log, CancellationToken ct)
    {
        if (!await store.DeleteTrackAsync(id, ct)) return SlideshowProblems.TrackNotFound();
        library.Invalidate();

        // Out of the trips that chose it (they'd skip it anyway), then its files.
        var trips = 0;
        foreach (var album in await store.ListAlbumsWithMusicAsync(ct))
        {
            if (!album.MusicTrackIds.Contains(id)) continue;
            await store.UpdateAlbumAsync(album.OwnerId, album.Id, a => a.MusicTrackIds = a.MusicTrackIds.Where(t => t != id).ToList(), ct);
            trips++;
        }
        try { await blobs.DeletePrefixAsync(BlobNames.TrackPrefix(id), ct); }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Track {TrackId} deleted; its files were left behind", id); }
        log.LogInformation("Music library: deleted track {TrackId}, used by {Trips} trips", id, trips);
        return Results.NoContent();
    }
}
