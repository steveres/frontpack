using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Slideshow.Api.Data;
using Slideshow.Api.Media;
using Slideshow.Api.Storage;
using Slideshow.Api.Tests.Infrastructure;
using static Slideshow.Api.Tests.AlbumTests;

namespace Slideshow.Api.Tests;

/// <summary>Smaller versions of photos (720, 1280, 1920px), made in the background after upload.</summary>
public sealed class ResizeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTime Taken = new(2026, 7, 1, 9, 0, 0);
    private static readonly (double, double) Rome = (41.90, 12.49);

    private async Task<(HttpClient Client, string Album, string UserId)> NewAlbumAsync()
    {
        var userId = Guid.NewGuid().ToString();
        var client = await factory.RegisteredClientAsync(userId);
        return (client, await CreateAlbumAsync(client, "Sizes"), userId);
    }

    /// <summary>The photo's sizes once the background worker has made them.</summary>
    private static async Task<int[]> WaitForSizesAsync(HttpClient client, JsonElement media, int count)
    {
        var info = media.GetProperty("url").GetString() + "/info";
        for (var waited = 0; ; waited += 50)
        {
            var sizes = (await client.GetFromJsonAsync<JsonElement>(info)).GetProperty("sizes").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            if (sizes.Length >= count) return sizes;
            if (waited > 20_000) throw new TimeoutException($"Still {sizes.Length} sizes, expected {count}.");
            await Task.Delay(50);
        }
    }

    private static async Task<(HttpResponseMessage Response, SKSizeI Size)> GetImageAsync(HttpClient client, string url)
    {
        var res = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var bitmap = SKBitmap.Decode(await res.Content.ReadAsByteArrayAsync());
        Assert.NotNull(bitmap);
        return (res, new SKSizeI(bitmap.Width, bitmap.Height));
    }

    private string SizedFile(string userId, string album, JsonElement media, int size) =>
        Path.Combine(factory.MediaRoot, userId, Guid.Parse(album).ToString("N"), Guid.Parse(media.GetProperty("id").GetString()!).ToString("N") + "." + size);

    [Fact]
    public async Task Photos_get_720_1280_and_1920_versions_after_upload()
    {
        var (client, album, _) = await NewAlbumAsync();
        var photo = await UploadAsync(client, album, TestMedia.RealJpeg(3000, 2000), "big.jpg");
        Assert.Equal(new[] { 720, 1280, 1920 }, await WaitForSizesAsync(client, photo, 3));

        var url = photo.GetProperty("url").GetString()!;
        foreach (var (size, expected) in new[] { (720, new SKSizeI(720, 480)), (1280, new SKSizeI(1280, 853)), (1920, new SKSizeI(1920, 1280)) })
        {
            var (res, actual) = await GetImageAsync(client, $"{url}?size={size}");
            Assert.Equal(expected, actual);
            Assert.Equal("image/jpeg", res.Content.Headers.ContentType!.MediaType);
            Assert.Equal($"\"{Guid.Parse(photo.GetProperty("id").GetString()!):N}-{size}\"", res.Headers.ETag!.Tag);
            Assert.Contains("immutable", res.Headers.CacheControl!.ToString());
        }
        Assert.Equal(new SKSizeI(3000, 2000), (await GetImageAsync(client, url)).Size); // no size: the original
    }

    [Fact]
    public async Task Photos_are_never_enlarged()
    {
        var (client, album, _) = await NewAlbumAsync();
        var small = TestMedia.RealJpeg(600, 400);
        var smallPhoto = await UploadAsync(client, album, small, "small.jpg");
        var original = TestMedia.RealJpeg(1500, 1000);
        var photo = await UploadAsync(client, album, original, "medium.jpg");
        Assert.Equal(new[] { 720, 1280 }, await WaitForSizesAsync(client, photo, 2)); // the worker goes in order: the small one is done too

        // Asking for 1920 gets the original: it's smaller than that already. Likewise any size of the small one.
        foreach (var (media, size, bytes) in new[] { (photo, 1920, original), (smallPhoto, 720, small) })
        {
            var res = await client.GetAsync(media.GetProperty("url").GetString() + "?size=" + size);
            Assert.Equal(bytes, await res.Content.ReadAsByteArrayAsync());
            Assert.Contains("immutable", res.Headers.CacheControl!.ToString());
        }
        Assert.Empty(await WaitForSizesAsync(client, smallPhoto, 0));
    }

    [Fact]
    public async Task Smaller_versions_are_upright_for_rotated_phone_photos()
    {
        // Stored landscape with EXIF orientation 6 ("rotate 90° clockwise"), as phones save portrait shots.
        var (client, album, _) = await NewAlbumAsync();
        var photo = await UploadAsync(client, album, TestMedia.RealJpeg(2000, 1000, orientation: 6), "portrait.jpg");
        await WaitForSizesAsync(client, photo, 3);
        Assert.Equal(new SKSizeI(360, 720), (await GetImageAsync(client, photo.GetProperty("url").GetString() + "?size=720")).Size);
    }

    [Fact]
    public async Task Until_they_are_made_the_original_is_sent_and_photos_from_before_get_queued()
    {
        var (client, album, userId) = await NewAlbumAsync();
        var original = TestMedia.RealJpeg(2400, 1600);
        var photo = await UploadAsync(client, album, original, "old.jpg");
        await WaitForSizesAsync(client, photo, 3);

        // Make it look like an upload from before smaller versions existed.
        foreach (var size in Renditions.Sizes) File.Delete(SizedFile(userId, album, photo, size));
        var id = Guid.Parse(photo.GetProperty("id").GetString()!);
        await factory.Services.GetRequiredService<IDataStore>()
            .SetResizeStateAsync(userId, Guid.Parse(album), id, ResizeState.None, [], CancellationToken.None);

        var url = photo.GetProperty("url").GetString()!;
        var res = await client.GetAsync(url + "?size=1280");
        Assert.Equal(original, await res.Content.ReadAsByteArrayAsync());
        Assert.True(res.Headers.CacheControl!.NoCache); // replaced by the smaller one once it's made

        Assert.Equal(new[] { 720, 1280, 1920 }, await WaitForSizesAsync(client, photo, 3));
        Assert.Equal(new SKSizeI(1280, 853), (await GetImageAsync(client, url + "?size=1280")).Size);
    }

    [Fact]
    public async Task Opening_an_album_queues_all_its_older_photos()
    {
        var (client, album, userId) = await NewAlbumAsync();
        var photos = new List<JsonElement>();
        for (var i = 0; i < 3; i++)
            photos.Add(await UploadAsync(client, album, TestMedia.RealJpeg(1600, 1200, Taken.AddMinutes(i), Rome), $"p{i}.jpg"));
        foreach (var photo in photos) await WaitForSizesAsync(client, photo, 2);
        (await client.PostAsync($"/api/v1/albums/{album}/compile", null)).EnsureSuccessStatusCode();

        // Make them all look like uploads from before smaller versions existed.
        var store = factory.Services.GetRequiredService<IDataStore>();
        foreach (var photo in photos)
        {
            foreach (var size in Renditions.Sizes) File.Delete(SizedFile(userId, album, photo, size));
            await store.SetResizeStateAsync(userId, Guid.Parse(album), Guid.Parse(photo.GetProperty("id").GetString()!), ResizeState.None, [], CancellationToken.None);
        }

        // Opening the slideshow queues every photo in it, not only the ones it goes on to show.
        (await client.GetAsync($"/api/v1/albums/{album}")).EnsureSuccessStatusCode();
        foreach (var photo in photos) Assert.Equal(new[] { 720, 1280 }, await WaitForSizesAsync(client, photo, 2));
    }

    [Fact]
    public async Task Unknown_sizes_are_rejected()
    {
        var (client, album, _) = await NewAlbumAsync();
        var photo = await UploadAsync(client, album, TestMedia.RealJpeg(100, 100), "p.jpg");
        foreach (var size in new[] { "100", "4000", "0", "-720" })
        {
            var res = await client.GetAsync(photo.GetProperty("url").GetString() + "?size=" + size);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.Equal("validation_failed", await AuthAndAccountTests.Code(res));
        }
    }

    [Fact]
    public async Task Videos_and_unreadable_photos_are_sent_as_they_are()
    {
        var (client, album, _) = await NewAlbumAsync();
        var mp4 = TestMedia.Mp4();
        var video = await UploadAsync(client, album, mp4, "clip.mp4");
        var broken = await UploadAsync(client, album, TestMedia.Jpeg(), "broken.jpg"); // valid header, no picture
        foreach (var (media, bytes) in new[] { (video, mp4), (broken, TestMedia.Jpeg()) })
        {
            Assert.Empty(media.GetProperty("sizes").EnumerateArray());
            var res = await client.GetAsync(media.GetProperty("url").GetString() + "?size=720");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal(bytes, await res.Content.ReadAsByteArrayAsync());
            Assert.Contains("immutable", res.Headers.CacheControl!.ToString()); // final: nothing smaller is coming
        }
    }

    [Fact]
    public async Task Deleting_a_photo_deletes_its_smaller_versions()
    {
        var (client, album, userId) = await NewAlbumAsync();
        var photo = await UploadAsync(client, album, TestMedia.RealJpeg(2000, 1500), "gone.jpg");
        await WaitForSizesAsync(client, photo, 3);
        Assert.All(Renditions.Sizes, s => Assert.True(File.Exists(SizedFile(userId, album, photo, s))));

        (await client.DeleteAsync(photo.GetProperty("url").GetString())).EnsureSuccessStatusCode();
        Assert.All(Renditions.Sizes, s => Assert.False(File.Exists(SizedFile(userId, album, photo, s))));
    }

    [Fact]
    public async Task Slideshows_list_the_sizes_made_since_they_were_compiled_and_share_links_serve_them()
    {
        var (client, album, _) = await NewAlbumAsync();
        await client.PostAsync($"/api/v1/albums/{album}/compile", null);
        var photo = await UploadAsync(client, album, TestMedia.RealJpeg(2000, 1000, Taken, Rome), "rome.jpg");
        (await client.PostAsync($"/api/v1/albums/{album}/compile", null)).EnsureSuccessStatusCode(); // likely before the sizes exist
        await WaitForSizesAsync(client, photo, 3);

        var manifest = await client.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}");
        Assert.Equal(new[] { 720, 1280, 1920 }, manifest.GetProperty("slides")[0].GetProperty("sizes").EnumerateArray().Select(e => e.GetInt32()));

        var token = (await (await client.PutAsJsonAsync($"/api/v1/albums/{album}/share", new { enabled = true }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var viewer = factory.CreateClient();
        var shared = await viewer.GetFromJsonAsync<JsonElement>($"/api/v1/shared/{token}");
        var slide = shared.GetProperty("slides")[0];
        Assert.Equal(3, slide.GetProperty("sizes").GetArrayLength());
        Assert.Equal(new SKSizeI(720, 360), (await GetImageAsync(viewer, slide.GetProperty("url").GetString() + "?size=720")).Size);
        Assert.Equal(HttpStatusCode.BadRequest, (await viewer.GetAsync(slide.GetProperty("url").GetString() + "?size=99")).StatusCode);
    }

    // ───────── The worker on its own: retries and giving up ─────────

    private sealed class FailingBlobs : IBlobStore
    {
        public Task UploadAsync(string name, Stream content, string contentType, CancellationToken ct) => Task.CompletedTask;
        public Task<Stream?> OpenReadAsync(string name, CancellationToken ct) => throw new IOException("storage unavailable");
        public Task DeleteAsync(string name, CancellationToken ct) => Task.CompletedTask;
        public Task DeletePrefixAsync(string prefix, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingQueue : IResizeQueue
    {
        public List<string> Calls { get; } = [];
        public Task EnqueueAsync(ResizeJob job, CancellationToken ct) { Calls.Add("enqueue"); return Task.CompletedTask; }
        public Task<QueuedJob?> ReceiveAsync(CancellationToken ct) => Task.FromResult<QueuedJob?>(null);
        public Task CompleteAsync(QueuedJob job, CancellationToken ct) { Calls.Add("complete"); return Task.CompletedTask; }
        public Task RetryLaterAsync(QueuedJob job, TimeSpan delay, CancellationToken ct) { Calls.Add($"retry {delay.TotalSeconds}s"); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Failures_are_retried_with_growing_delays_then_given_up_on()
    {
        var store = new LocalDataStore(Options.Create(new TablesOptions { LocalPath = "" }));
        var media = new MediaFile
        {
            Id = Guid.NewGuid(), AlbumId = Guid.NewGuid(), OwnerId = "owner", Kind = MediaKind.Image,
            FileName = "p.jpg", ContentType = "image/jpeg", BlobName = "owner/a/p", ResizeState = ResizeState.Pending,
        };
        await store.AddMediaAsync(media, CancellationToken.None);
        var queue = new RecordingQueue();
        var worker = new ResizeWorker(queue, store, new FailingBlobs(), Options.Create(new ResizeOptions { MaxAttempts = 3 }), NullLogger<ResizeWorker>.Instance);
        var job = new ResizeJob(media.OwnerId, media.AlbumId, media.Id);

        for (var attempt = 1; attempt <= 3; attempt++)
            await worker.HandleAsync(new QueuedJob(job, attempt, attempt), CancellationToken.None);

        Assert.Equal(["retry 30s", "retry 60s", "complete"], queue.Calls);
        Assert.Equal(ResizeState.Unavailable, (await store.GetMediaAsync(media.OwnerId, media.AlbumId, media.Id, CancellationToken.None))!.ResizeState);
    }

    [Fact]
    public async Task Jobs_for_deleted_photos_are_dropped()
    {
        var store = new LocalDataStore(Options.Create(new TablesOptions { LocalPath = "" }));
        var queue = new RecordingQueue();
        var worker = new ResizeWorker(queue, store, new FailingBlobs(), Options.Create(new ResizeOptions()), NullLogger<ResizeWorker>.Instance);
        await worker.HandleAsync(new QueuedJob(new ResizeJob("owner", Guid.NewGuid(), Guid.NewGuid()), 1, 1), CancellationToken.None);
        Assert.Equal(["complete"], queue.Calls);
    }
}
