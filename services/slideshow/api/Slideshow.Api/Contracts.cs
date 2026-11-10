using System.Text.Json.Serialization;
using Slideshow.Api.Data;

namespace Slideshow.Api;

public static class Limits
{
    public const int AlbumNameLength = 200;
    public const int AlbumDescriptionLength = 2000;
    public const int FileNameLength = 255;
    public const int DeleteBatch = 5000; // = the most files an album can hold
    public const int TracksPerTrip = 20;
    public const int TrackTextLength = 200;  // title, artist, license name
    public const int UrlLength = 500;
    public const int TagsPerTrack = 12;
    public const int TagLength = 40;
    public const int LicenseBytes = 64 * 1024;
}


public sealed record CreateAlbumRequest(string? Name, string? Description);

/// <summary>Rename an album or change its description. Omitted fields stay as they are; an empty description removes it.</summary>
public sealed record UpdateAlbumRequest(string? Name, string? Description);

/// <summary>Several files of one collection to delete at once (at most <see cref="Limits.DeleteBatch"/>).</summary>
public sealed record DeleteMediaRequest(IReadOnlyList<Guid>? Ids);
/// <param name="Deleted">How many of the files existed and were deleted.</param>
public sealed record DeleteMediaResult(int Deleted);

/// <param name="ExcludedCount">Photos/videos the slideshow leaves out because they have no date or no location.</param>
/// <param name="CoverId">The photo shown on the album's card: the first in slideshow order, else the first uploaded.</param>
public sealed record AlbumInfoDto(
    Guid Id, string Name, string? Description, DateTime CreatedAt, DateTime UpdatedAt,
    int ImageCount, int VideoCount, int MusicCount, int ExcludedCount, long TotalBytes, DateTime? CompiledAt, bool IsStale,
    bool IsShared, bool IsPublic,
    [property: JsonIgnore] Guid? CoverId)
{
    public string? CoverThumbnailUrl => CoverId is { } c ? $"/api/v1/albums/{Id}/images/{c}/thumbnail" : null;
}

public sealed record LocationDto(double Lat, double Lon);

/// <param name="Playable">Whether the slideshow shows it. Photos and videos need a date and a location; music always plays.</param>
/// <param name="Missing">Why it isn't playable: "date" and/or "location".</param>
/// <param name="ThumbnailUrl">A small JPEG preview (photos only; null when there is none).</param>
/// <param name="Sizes">The photo's smaller versions made so far (longest edge in pixels; `?size=` on its url), smallest first.</param>
/// <param name="DurationSeconds">Videos: running time in seconds; null when unknown, and for photos.</param>
public sealed record MediaInfoDto(
    Guid Id, string FileName, string Kind, string ContentType, long SizeBytes, DateTime UploadedAt,
    [property: JsonConverter(typeof(LocalDateTimeConverter))] DateTime? Taken,
    string? TakenSource, LocationDto? Location, bool Playable, IReadOnlyList<string> Missing,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url = null,
    string? ThumbnailUrl = null, IReadOnlyList<int>? Sizes = null, double? DurationSeconds = null)
{
    public static MediaInfoDto From(MediaFile m, string? url = null)
    {
        var missing = MissingFor(m);
        var hasThumbnail = url is not null && m.Kind == MediaKind.Image && m.ThumbnailState != ThumbnailState.Unavailable;
        return new(
            m.Id, m.FileName, m.Kind.ToString().ToLowerInvariant(), m.ContentType, m.SizeBytes, m.UploadedAt,
            m.TakenAt, m.TakenSource?.ToString() switch { null => null, var s => char.ToLowerInvariant(s[0]) + s[1..] },
            m.Latitude is { } lat && m.Longitude is { } lon ? new LocationDto(lat, lon) : null,
            missing.Count == 0, missing, url, hasThumbnail ? url + "/thumbnail" : null, m.Kind == MediaKind.Image ? m.Sizes : [],
            m.Kind == MediaKind.Video && m.DurationSeconds > 0 ? m.DurationSeconds : null);
    }

    /// <summary>What a slide lacks to be shown: the slideshow plays in date order along a route, so it needs both.</summary>
    public static List<string> MissingFor(MediaFile m)
    {
        var missing = new List<string>(2);
        if (m.Collection != MediaCollection.Images) return missing;
        if (m.TakenAt is null) missing.Add("date");
        if (m.Latitude is null || m.Longitude is null) missing.Add("location");
        return missing;
    }
}

/// <param name="Slides">Playable photos and videos in play order.</param>
/// <param name="Music">The trip's music in play order: as chosen now (it isn't part of the compiled slideshow).</param>
/// <param name="ExcludedCount">Photos/videos left out (no date or no location).</param>
public sealed record AlbumManifestDto(
    Guid AlbumId, string Name, DateTime CompiledAt, bool IsStale,
    IReadOnlyList<MediaInfoDto> Slides, IReadOnlyList<TrackDto> Music, int ExcludedCount);

/// <summary>A track of the music library, as anyone sees it. `Url` plays it and `LicenseUrl` shows its license; no sign-in needed.</summary>
public record TrackDto(
    Guid Id, string Title, string Artist, string? ArtistUrl, string? SourceUrl, IReadOnlyList<string> Tags,
    double DurationSeconds, string LicenseName, string Url, string LicenseUrl)
{
    public static TrackDto From(MusicTrack t) => new(
        t.Id, t.Title, t.Artist, t.ArtistUrl, t.SourceUrl, t.Tags, t.DurationSeconds, t.LicenseName,
        $"/api/v1/music/{t.Id}?v={t.FileVersion}", $"/api/v1/music/{t.Id}/license");
}

/// <summary>A library track as music admins see it: also its files and how many trips use it.</summary>
public sealed record AdminTrackDto(
    Guid Id, string Title, string Artist, string? ArtistUrl, string? SourceUrl, IReadOnlyList<string> Tags,
    double DurationSeconds, string LicenseName, string Url, string LicenseUrl,
    string LicenseFileName, long SizeBytes, DateTime AddedAt, DateTime UpdatedAt, int UsedBy)
    : TrackDto(Id, Title, Artist, ArtistUrl, SourceUrl, Tags, DurationSeconds, LicenseName, Url, LicenseUrl)
{
    public static AdminTrackDto From(MusicTrack t, int usedBy)
    {
        var d = TrackDto.From(t);
        return new(d.Id, d.Title, d.Artist, d.ArtistUrl, d.SourceUrl, d.Tags, d.DurationSeconds, d.LicenseName, d.Url, d.LicenseUrl,
            t.LicenseFileName, t.SizeBytes, t.AddedAt, t.UpdatedAt, usedBy);
    }
}

/// <summary>A trip's music: library track ids in play order (at most <see cref="Limits.TracksPerTrip"/>, no repeats).</summary>
public sealed record AlbumMusicRequest(IReadOnlyList<Guid>? TrackIds);

/// <param name="Token">The secret code for the share link (the website builds the link); null when not shared.</param>
/// <param name="IsPublic">Listed on the Albums page for everyone (only while shared).</param>
public sealed record ShareDto(bool Enabled, string? Token, bool ShowMap, bool IsPublic);

/// <summary>
/// Turn link sharing on/off, choose whether viewers see the map, and list the album for everyone (public).
/// Omitted fields stay as they are. Making it public shares it; turning sharing off makes it private again.
/// </summary>
public sealed record UpdateShareRequest(bool? Enabled, bool? ShowMap, bool? IsPublic);

/// <summary>A public album as anyone sees it: what's in its slideshow, and the share code that plays it.</summary>
/// <param name="ImageCount">Photos in the slideshow (photos without a date or location stay private, so aren't counted).</param>
/// <param name="CoverThumbnailUrl">Preview of the slideshow's first photo, under /api/v1/shared/{token}; null when there is none.</param>
public sealed record PublicAlbumDto(
    Guid Id, string Name, string? Description, DateTime CreatedAt, DateTime UpdatedAt,
    int ImageCount, int VideoCount, int MusicCount, string ShareToken, string? CoverThumbnailUrl);

/// <summary>What anonymous viewers of a share link get: the slideshow only, with file URLs under /api/v1/shared/{token}.</summary>
public sealed record SharedAlbumDto(string Name, bool ShowMap, IReadOnlyList<MediaInfoDto> Slides, IReadOnlyList<TrackDto> Music);

/// <summary>Writes "taken" as local wall-clock time with no zone ("2026-07-04T09:31:05"), as it was recorded.</summary>
public sealed class LocalDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
        reader.TokenType == System.Text.Json.JsonTokenType.Null ? null : DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Unspecified);

    public override void Write(System.Text.Json.Utf8JsonWriter writer, DateTime? value, System.Text.Json.JsonSerializerOptions options)
    {
        if (value is { } v) writer.WriteStringValue(v.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        else writer.WriteNullValue();
    }
}

/// <summary>Slideshow's own errors (the general ones are in FrontPack.ServiceDefaults.Problems).</summary>
public static class SlideshowProblems
{
    public static IResult AlbumNotFound() => Problems.Of(404, "album_not_found", "Trip not found.");
    public static IResult MediaNotFound() => Problems.Of(404, "media_not_found", "File not found.");
    public static IResult TrackNotFound() => Problems.Of(404, "track_not_found", "That track isn't in the music library.");
}
