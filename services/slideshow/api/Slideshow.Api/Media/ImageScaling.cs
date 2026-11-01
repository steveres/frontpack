using SkiaSharp;

namespace Slideshow.Api.Media;

/// <summary>Decoding photos at reduced size, shrinking them and saving them upright as JPEG (previews and smaller versions).</summary>
internal static class ImageScaling
{
    private static readonly SKColor Background = new(0x14, 0x18, 0x1d); // behind transparent PNGs; matches the site

    /// <summary>A decoded photo, as stored (not yet turned upright).</summary>
    public sealed class Decoded(SKBitmap bitmap, SKEncodedOrigin origin, int frames, int fullLongestEdge) : IDisposable
    {
        public SKBitmap Bitmap { get; } = bitmap;
        public SKEncodedOrigin Origin { get; } = origin;
        /// <summary>More than 1 for animations (GIF, WebP); only the first frame is decoded.</summary>
        public int Frames { get; } = frames;
        /// <summary>The longest edge of the photo at full size.</summary>
        public int FullLongestEdge { get; } = fullLongestEdge;
        public void Dispose() => Bitmap.Dispose();
    }

    /// <summary>
    /// Decodes `source`, at reduced size where the format allows (JPEG: 1/2, 1/4, 1/8) but at least
    /// 2x `maxEdge`; null if the format can't be decoded here (e.g. AVIF) or the file is damaged.
    /// </summary>
    public static Decoded? TryDecode(Stream source, int maxEdge)
    {
        // SKCodec takes ownership of the stream it reads; the wrapper keeps the caller's stream open.
        using var codec = SKCodec.Create(new SKManagedStream(source, disposeManagedStream: false));
        if (codec is null) return null;
        var full = codec.Info;
        if (full.Width <= 0 || full.Height <= 0) return null;

        var scale = Math.Min(1f, (float)maxEdge / Math.Max(full.Width, full.Height));
        var decodeSize = codec.GetScaledDimensions(Math.Min(1f, scale * 2));
        var decodeInfo = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var decoded = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            decoded.Dispose();
            return null;
        }
        return new Decoded(decoded, codec.EncodedOrigin, codec.FrameCount, Math.Max(full.Width, full.Height));
    }

    /// <summary>`source` shrunk to at most `maxEdge` on its longest side (never enlarged); null if Skia can't.</summary>
    public static SKBitmap? Shrink(SKBitmap source, int maxEdge)
    {
        var f = Math.Min(1f, (float)maxEdge / Math.Max(source.Width, source.Height));
        int w = Math.Max(1, (int)Math.Round(source.Width * f)), h = Math.Max(1, (int)Math.Round(source.Height * f));
        return source.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell));
    }

    /// <summary>`bitmap` (stored with EXIF orientation `origin`) turned upright and saved as JPEG.</summary>
    public static byte[]? UprightJpeg(SKBitmap bitmap, SKEncodedOrigin origin, int quality)
    {
        int w = bitmap.Width, h = bitmap.Height;
        var sideways = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        using var surface = SKSurface.Create(new SKImageInfo(sideways ? h : w, sideways ? w : h, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(Background);
        canvas.SetMatrix(Orientation(origin, w, h));
        using (var image = SKImage.FromBitmap(bitmap))
            canvas.DrawImage(image, 0, 0, SKSamplingOptions.Default); // same size: nothing to resample

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data?.ToArray();
    }

    /// <summary>Maps a w×h image stored with EXIF orientation `origin` onto an upright canvas.</summary>
    private static SKMatrix Orientation(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        // x' = ScaleX·x + SkewX·y + TransX;  y' = SkewY·x + ScaleY·y + TransY
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),      // mirrored
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),  // 180°
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),    // flipped
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),        // transposed
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),      // 90° clockwise (portrait phone photo)
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),  // transverse
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),    // 90° counter-clockwise
        _ => SKMatrix.Identity,
    };
}
