using Slideshow.Api.Data;

namespace Slideshow.Api.Media;

/// <summary>
/// A trip's cover photo: the one its owner chose, else the first photo in slideshow order. Only a photo the
/// slideshow shows can be the cover, so it's never something viewers of a share link couldn't see anyway.
/// </summary>
public static class Covers
{
    /// <summary>A photo (not a video) in the slideshow, with a preview.</summary>
    public static bool CanBeCover(MediaFile m) =>
        m.Collection == MediaCollection.Images && m.Kind == MediaKind.Image
        && m.ThumbnailState != ThumbnailState.Unavailable && MediaInfoDto.MissingFor(m).Count == 0;

    /// <summary>The chosen cover while it can still be one, else the slideshow's first photo; null when it has none.</summary>
    public static MediaFile? For(Album album, IEnumerable<MediaFile> media)
    {
        var photos = media.Where(CanBeCover).ToList();
        return photos.FirstOrDefault(m => m.Id == album.CoverMediaId)
            ?? photos.OrderBy(m => m.TakenAt).ThenBy(m => m.FileName, StringComparer.Ordinal).FirstOrDefault();
    }
}
