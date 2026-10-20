namespace Slideshow.Api.Media;

/// <summary>
/// Smaller versions of photos for small screens and slow connections, made in the background after
/// upload (see ResizeWorker) and stored next to the photo as `{blob}.{size}`.
/// </summary>
public static class Renditions
{
    /// <summary>Longest edge in pixels of each smaller version. A photo gets only the ones smaller than itself.</summary>
    public static readonly IReadOnlyList<int> Sizes = [720, 1280, 1920];
    private const int Quality = 82;
    public const string ContentType = "image/jpeg";

    /// <summary>
    /// JPEGs of the image in `source` at each of <see cref="Sizes"/> smaller than it, upright per its
    /// EXIF orientation: none for photos of 720px or less. Null if it can't be decoded here (e.g. AVIF),
    /// is damaged, or is animated (a still copy would lose the animation).
    /// </summary>
    public static IReadOnlyList<(int Size, byte[] Jpeg)>? TryCreate(Stream source)
    {
        try
        {
            using var decoded = ImageScaling.TryDecode(source, Sizes[^1]);
            if (decoded is null || decoded.Frames > 1) return null;

            // Largest first, each made from the one before: no step shrinks by more than about 2x,
            // which keeps the cubic resampling free of aliasing.
            var made = new List<(int, byte[])>();
            var from = decoded.Bitmap;
            try
            {
                foreach (var size in Sizes.Where(s => s < decoded.FullLongestEdge).OrderDescending())
                {
                    var resized = ImageScaling.Shrink(from, size);
                    if (resized is null) return null;
                    if (from != decoded.Bitmap) from.Dispose();
                    from = resized;
                    var jpeg = ImageScaling.UprightJpeg(resized, decoded.Origin, Quality);
                    if (jpeg is null) return null;
                    made.Add((size, jpeg));
                }
            }
            finally
            {
                if (from != decoded.Bitmap) from.Dispose();
            }
            made.Reverse(); // smallest first
            return made;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }
}
