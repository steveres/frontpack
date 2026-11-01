namespace Slideshow.Api.Media;

/// <summary>Small JPEG previews of photos for the album pages.</summary>
public static class Thumbnails
{
    /// <summary>Longest edge in pixels: about 2x the size shown, for sharp high-DPI screens.</summary>
    public const int MaxEdge = 400;
    private const int Quality = 80;
    public const string ContentType = "image/jpeg";

    /// <summary>
    /// A JPEG thumbnail of the image in `source`, upright per its EXIF orientation; null if the format
    /// can't be decoded here (e.g. AVIF) or the file is damaged.
    /// </summary>
    public static byte[]? TryCreate(Stream source)
    {
        try
        {
            using var decoded = ImageScaling.TryDecode(source, MaxEdge);
            if (decoded is null) return null;
            using var resized = ImageScaling.Shrink(decoded.Bitmap, MaxEdge);
            return resized is null ? null : ImageScaling.UprightJpeg(resized, decoded.Origin, Quality);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }
}
