namespace Slideshow.Api.Data;

public sealed class Album
{
    public Guid Id { get; set; }
    public required string OwnerId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Bumped on every add/delete of media; compared with CompiledVersion to detect staleness.</summary>
    public int ContentVersion { get; set; }
    public int? CompiledVersion { get; set; }
    /// <summary>When the manifest (a blob next to the album's files) was last written.</summary>
    public DateTime? CompiledAt { get; set; }
    /// <summary>Link sharing: the secret code in the album's share link; null when not shared.</summary>
    public string? ShareToken { get; set; }
    /// <summary>Viewers of the share link don't see the map (or the photos' locations). Default: they do.</summary>
    public bool ShareHideMap { get; set; }
    /// <summary>Listed on the Albums page for everyone. Only while shared by link: the share link is how they play it.</summary>
    public bool IsPublic { get; set; }
    /// <summary>The slideshow's music: tracks from the music library, in play order. Ids no longer in the library are skipped.</summary>
    public IReadOnlyList<Guid> MusicTrackIds { get; set; } = [];

    /// <summary>The stored row's version when read; an update fails if the row changed since (not stored itself).</summary>
    public string? ETag { get; set; }

    internal Album Clone() => (Album)MemberwiseClone();
}

/// <summary>
/// Which album collection a file belongs to. Music (1) was uploaded MP3s: music now comes from the music
/// library, and LibraryMigration deletes any left from before.
/// </summary>
public enum MediaCollection { Images = 0, Music = 1 }

public enum MediaKind { Image = 0, Video = 1, Audio = 2 }

/// <summary>Where the capture date came from. Only real capture dates are used, never file times.</summary>
public enum TakenSource { Exif = 0, Video = 1 }

/// <summary>Whether a photo's preview has been made. Videos stay None (no thumbnails yet).</summary>
public enum ThumbnailState { None = 0, Ready = 1, Unavailable = 2 }

/// <summary>
/// Whether a photo's smaller versions have been made (in the background, after upload). None: not asked
/// for yet (photos uploaded before they existed; videos and music never are).
/// </summary>
public enum ResizeState { None = 0, Pending = 1, Ready = 2, Unavailable = 3 }

public sealed class MediaFile
{
    public Guid Id { get; set; }
    public Guid AlbumId { get; set; }
    public required string OwnerId { get; set; }
    public MediaCollection Collection { get; set; }
    public MediaKind Kind { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string BlobName { get; set; }
    public DateTime UploadedAt { get; set; }
    /// <summary>Local wall-clock time the photo/video was taken (no time zone, like EXIF).</summary>
    public DateTime? TakenAt { get; set; }
    public TakenSource? TakenSource { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public ThumbnailState ThumbnailState { get; set; }
    public ResizeState ResizeState { get; set; }
    /// <summary>The smaller versions made so far (longest edge in pixels), smallest first.</summary>
    public IReadOnlyList<int> Sizes { get; set; } = [];
    /// <summary>Videos: running time in seconds; 0 when the file doesn't say; null when not read yet (uploaded before it was).</summary>
    public double? DurationSeconds { get; set; }

    internal MediaFile Clone() => (MediaFile)MemberwiseClone();
}

/// <summary>A track in the music library, which trips choose their music from. Added by music admins.</summary>
public sealed class MusicTrack
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Artist { get; set; }
    public string? ArtistUrl { get; set; }
    /// <summary>Where it was found, e.g. its Pixabay page.</summary>
    public string? SourceUrl { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
    public double DurationSeconds { get; set; }
    /// <summary>e.g. "Pixabay Content License".</summary>
    public required string LicenseName { get; set; }
    /// <summary>The license certificate's original file name (its text is a blob, see BlobNames.TrackLicense).</summary>
    public required string LicenseFileName { get; set; }
    public long SizeBytes { get; set; }
    /// <summary>Bumped when the MP3 is replaced: part of its blob name and URL, so caches never serve the old one.</summary>
    public int FileVersion { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>The admin who added it (user id).</summary>
    public string? AddedBy { get; set; }

    internal MusicTrack Clone() => (MusicTrack)MemberwiseClone();
}
