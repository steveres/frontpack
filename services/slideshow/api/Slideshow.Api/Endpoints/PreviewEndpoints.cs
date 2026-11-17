using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Slideshow.Api.Data;
using Slideshow.Api.Media;
using Slideshow.Api.Storage;

namespace Slideshow.Api.Endpoints;

/// <summary>
/// Share links: https://s.thefrontpack.com/{code}. Apps that show a preview of a link (LinkedIn, Messages,
/// Slack, …) read the page without running scripts, so the website's slideshow page (filled in by script)
/// would preview as just "Slideshow". This page has the trip's name, description and cover photo in its
/// Open Graph tags, and sends people's browsers on to the slideshow on the website.
/// </summary>
public static partial class PreviewEndpoints
{
    public static void MapSharePreviews(this RouteGroupBuilder g)
    {
        g.WithTags("Shared");
        g.MapMethods("/{token}", ["GET", "HEAD"], Page).WithSummary("A share link: a page with the trip's preview, which opens its slideshow on the website.");
        g.MapMethods("/{token}/cover.jpg", ["GET", "HEAD"], Cover).WithSummary("The shared trip's cover photo, for link previews.");
    }

    /// <summary>The page's only script: on to the slideshow (the link's address). Nothing from users is in it.</summary>
    private const string Redirect = "location.replace(document.getElementById('watch').href)";
    private static readonly string RedirectHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Redirect)));

    /// <summary>
    /// Apps fetching a link to preview it. Some (Messages among them, which calls itself facebookexternalhit)
    /// load it in a real browser engine, which would follow the redirect before reading the tags: they get none.
    /// </summary>
    [GeneratedRegex("bot|facebookexternalhit|facebot|slack|whatsapp|telegram|discord|skype|embedly|preview", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPreviewer();

    /// <summary>Link previews show the cover at most about 1200 pixels wide.</summary>
    private const int CoverSize = 1280;

    private static async Task<IResult> Page(string token, HttpContext http, ISlideshowStore store,
        IOptions<SharingOptions> sharing, IOptions<CorsOptions> cors, CancellationToken ct)
    {
        var site = SiteOrigin(sharing.Value, cors.Value);
        var watch = $"{site}/slideshow.html?share={Uri.EscapeDataString(token)}";
        var self = $"{PublicOrigin(http)}/{Uri.EscapeDataString(token)}";

        // A wrong or turned-off link still goes to the slideshow page, which says so.
        var album = await SharingEndpoints.SharedAlbum(store, token, ct);
        var cover = album is null ? null : Covers.For(album, await store.ListMediaAsync(album.OwnerId, album.Id, ct));
        var title = album?.Name ?? "FrontPack";
        var description = album is null ? "This slideshow isn't shared, or the link is wrong."
            : string.IsNullOrWhiteSpace(album.Description) ? "A slideshow on FrontPack." : Shorten(album.Description, 300);
        // The cover's id in its address: apps keep previews by image address, so choosing another cover shows anew.
        var image = cover is null ? $"{site}/images/icon.png" : $"{self}/cover.jpg?v={cover.Id.ToString("N")[..12]}";

        string E(string s) => WebUtility.HtmlEncode(s);
        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{E(title)}</title>
            <meta name="description" content="{E(description)}">
            <meta property="og:type" content="website">
            <meta property="og:site_name" content="FrontPack">
            <meta property="og:title" content="{E(title)}">
            <meta property="og:description" content="{E(description)}">
            <meta property="og:url" content="{E(self)}">
            <meta property="og:image" content="{E(image)}">
            <meta property="og:image:alt" content="{E(album is null ? "FrontPack" : $"A photo from {album.Name}")}">
            <meta name="twitter:card" content="{(cover is null ? "summary" : "summary_large_image")}">
            <style>body{"{"}font:18px system-ui,sans-serif;margin:3rem 1rem;text-align:center{"}"}</style>
            {(LinkPreviewer().IsMatch(http.Request.Headers.UserAgent.ToString()) ? "" : $"<script>{Redirect}</script>")}
            </head>
            <body><p><a id="watch" href="{E(watch)}">Watch “{E(title)}”</a></p></body>
            </html>
            """;

        var headers = http.Response.Headers;
        // Instead of the API's own (no page at all): this page's style and its one script, and nothing else.
        headers.ContentSecurityPolicy = $"default-src 'none'; style-src 'unsafe-inline'; script-src 'sha256-{RedirectHash}'; frame-ancestors 'none'; base-uri 'none'";
        headers.CacheControl = "public, max-age=300"; // a renamed trip, or a turned-off link, shows within minutes
        return Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8, album is null ? 404 : 200);
    }

    private static async Task<IResult> Cover(string token, HttpContext http, ISlideshowStore store, IBlobStore blobs, CancellationToken ct)
    {
        var album = await SharingEndpoints.SharedAlbum(store, token, ct);
        if (album is null) return SharingEndpoints.NotShared();
        var cover = Covers.For(album, await store.ListMediaAsync(album.OwnerId, album.Id, ct));
        if (cover is null) return SlideshowProblems.MediaNotFound();

        const string cache = "public, max-age=3600";
        // A smaller version of the photo (never the original: it can be large, or a format apps can't show),
        // else its preview while those are being made.
        var size = cover.Sizes.Where(s => s >= CoverSize).Order().FirstOrDefault();
        if (size == 0 && cover.Sizes.Count > 0) size = cover.Sizes.Max();
        if (size > 0 && await blobs.OpenReadAsync(BlobNames.Sized(cover.BlobName, size), ct) is { } stream)
        {
            http.Response.Headers.CacheControl = cache;
            return Results.Stream(stream, Renditions.ContentType, entityTag: new EntityTagHeaderValue($"\"{cover.Id:N}-{size}\""));
        }
        return await MediaEndpoints.ThumbnailAsync(http, store, blobs, cover, cache, ct);
    }

    /// <summary>Where the slideshow page is: configured, else the website the API lets call it.</summary>
    private static string SiteOrigin(SharingOptions sharing, CorsOptions cors) =>
        (string.IsNullOrWhiteSpace(sharing.SiteOrigin) ? cors.AllowedOrigins.FirstOrDefault() ?? "" : sharing.SiteOrigin).TrimEnd('/');

    /// <summary>This API's address as the caller used it (in Azure, HTTPS ends at the ingress in front of it).</summary>
    private static string PublicOrigin(HttpContext http) =>
        $"{http.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? http.Request.Scheme}://{http.Request.Host}";

    private static string Shorten(string text, int max)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
    }
}
