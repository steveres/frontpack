using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Slideshow.Api.Data;
using Slideshow.Api.Media;
using Slideshow.Api.Storage;
using Slideshow.Api.Tests.Infrastructure;
using static Slideshow.Api.Tests.AlbumTests;

namespace Slideshow.Api.Tests;

/// <summary>The music library: music admins keep it, trips choose from it, anyone can play it.</summary>
public sealed class MusicTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    internal const string License = "PIXABAY LICENSE CERTIFICATE\nAudio File Title: Test\n";

    /// <summary>The admin page's form: an MP3, its license certificate and the details. Leave a file out with null.</summary>
    internal static MultipartFormDataContent TrackForm(Dictionary<string, string> fields, byte[]? mp3, string? license = License)
    {
        var form = new MultipartFormDataContent();
        foreach (var (k, v) in fields) form.Add(new StringContent(v), k);
        if (mp3 is not null) form.Add(new ByteArrayContent(mp3), "file", "song.mp3");
        if (license is not null) form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(license)), "license", "song-license.txt");
        return form;
    }

    /// <summary>Adds a library track (as a music admin); returns it as admins see it.</summary>
    internal static async Task<JsonElement> AddTrackAsync(HttpClient admin, string title, double seconds = 120, string tags = "folk, Upbeat")
    {
        var res = await admin.PostAsync("/api/v1/admin/music", TrackForm(new()
        {
            ["title"] = title, ["artist"] = "Tester", ["licenseName"] = "Pixabay Content License",
            ["durationSeconds"] = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), ["tags"] = tags,
            ["sourceUrl"] = "https://pixabay.com/music/test-1/",
        }, TestMedia.Mp3()));
        Assert.True(res.StatusCode == HttpStatusCode.Created, $"{res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string Id(JsonElement e) => e.GetProperty("id").GetString()!;

    [Fact]
    public async Task Only_music_admins_can_see_or_change_the_library_admin()
    {
        var user = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/admin/music/access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/admin/music")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/v1/admin/music", TrackForm([], TestMedia.Mp3()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/admin/music")).StatusCode);

        var admin = await factory.MusicAdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/admin/music/access")).StatusCode);
        var track = await AddTrackAsync(admin, "Admins only");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/v1/admin/music/{Id(track)}")).StatusCode);
    }

    [Fact]
    public async Task A_track_needs_an_mp3_a_license_certificate_and_its_details()
    {
        var admin = await factory.MusicAdminClientAsync();
        var fields = new Dictionary<string, string> { ["title"] = "T", ["artist"] = "A", ["licenseName"] = "L", ["durationSeconds"] = "60" };

        var noLicense = await admin.PostAsync("/api/v1/admin/music", TrackForm(fields, TestMedia.Mp3(), license: null));
        Assert.Equal(HttpStatusCode.BadRequest, noLicense.StatusCode);
        Assert.Contains("license", await noLicense.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/admin/music", TrackForm(fields, mp3: null))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await admin.PostAsync("/api/v1/admin/music", TrackForm(fields, TestMedia.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/admin/music",
            TrackForm(new(fields) { ["title"] = "" }, TestMedia.Mp3()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/admin/music",
            TrackForm(new(fields) { ["sourceUrl"] = "javascript:alert(1)" }, TestMedia.Mp3()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/admin/music",
            TrackForm(fields.Where(f => f.Key != "durationSeconds").ToDictionary(), TestMedia.Mp3()))).StatusCode);
    }

    [Fact]
    public async Task Admins_add_change_export_and_delete_tracks_and_anyone_can_play_them()
    {
        var admin = await factory.MusicAdminClientAsync();
        var added = await AddTrackAsync(admin, "Sunny Road", 154.5);
        Assert.Equal(["folk", "upbeat"], added.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal("song-license.txt", added.GetProperty("licenseFileName").GetString());
        Assert.Equal(0, added.GetProperty("usedBy").GetInt32());

        // Anyone can list it, play it and read its license.
        var anonymous = factory.CreateClient();
        var listed = (await anonymous.GetFromJsonAsync<JsonElement[]>("/api/v1/music"))!.Single(t => Id(t) == Id(added));
        Assert.Equal(154.5, listed.GetProperty("durationSeconds").GetDouble());
        Assert.False(listed.TryGetProperty("usedBy", out _)); // only admins see that
        var play = await anonymous.GetAsync(listed.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, play.StatusCode);
        Assert.Equal("audio/mpeg", play.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TestMedia.Mp3(), await play.Content.ReadAsByteArrayAsync());
        Assert.Equal(License, await anonymous.GetStringAsync(listed.GetProperty("licenseUrl").GetString()));

        // Changing the details keeps the file; a new MP3 gets a new address (caches never play the old one).
        var changed = await admin.PostAsync($"/api/v1/admin/music/{Id(added)}", TrackForm(new() { ["title"] = "Sunny Road (edit)", ["tags"] = "calm" }, mp3: null, license: null));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var edited = await changed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sunny Road (edit)", edited.GetProperty("title").GetString());
        Assert.Equal("Tester", edited.GetProperty("artist").GetString());
        Assert.Equal(listed.GetProperty("url").GetString(), edited.GetProperty("url").GetString());
        var replaced = await (await admin.PostAsync($"/api/v1/admin/music/{Id(added)}",
            TrackForm(new() { ["durationSeconds"] = "99" }, [.. TestMedia.Mp3(), 1, 2, 3], license: null))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(edited.GetProperty("url").GetString(), replaced.GetProperty("url").GetString());
        Assert.Equal(99, replaced.GetProperty("durationSeconds").GetDouble());

        var export = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/music/export");
        var exported = export.GetProperty("tracks").EnumerateArray().Single(t => Id(t.GetProperty("track")) == Id(added));
        Assert.Equal(License, exported.GetProperty("licenseText").GetString());

        // A trip that chose it plays without it once it's deleted.
        var owner = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        var album = await CreateAlbumAsync(owner, "Uses it");
        (await owner.PutAsJsonAsync($"/api/v1/albums/{album}/music", new { trackIds = new[] { Id(added) } })).EnsureSuccessStatusCode();
        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/admin/music"))!.Single(t => Id(t) == Id(added)).GetProperty("usedBy").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/admin/music/{Id(added)}")).StatusCode);
        Assert.DoesNotContain((await anonymous.GetFromJsonAsync<JsonElement[]>("/api/v1/music"))!, t => Id(t) == Id(added));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(replaced.GetProperty("url").GetString())).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement[]>($"/api/v1/albums/{album}/music"))!);
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}/info")).GetProperty("musicCount").GetInt32());
        Assert.Empty(Directory.GetDirectories(factory.MediaRoot, Guid.Parse(Id(added)).ToString("N"), SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Trips_choose_library_tracks_in_the_order_they_play()
    {
        var admin = await factory.MusicAdminClientAsync();
        var a = Id(await AddTrackAsync(admin, "Track A"));
        var b = Id(await AddTrackAsync(admin, "Track B"));
        var owner = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        var album = await CreateAlbumAsync(owner, "Music trip");
        await UploadAsync(owner, album, TestMedia.Jpeg(new DateTime(2026, 7, 1, 9, 0, 0), (45.0, 9.0)), "p.jpg");

        var put = await owner.PutAsJsonAsync($"/api/v1/albums/{album}/music", new { trackIds = new[] { b, a } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(["Track B", "Track A"], (await put.Content.ReadFromJsonAsync<JsonElement[]>())!.Select(t => t.GetProperty("title").GetString()));
        Assert.Equal([b, a], (await owner.GetFromJsonAsync<JsonElement[]>($"/api/v1/albums/{album}/music"))!.Select(Id));
        Assert.Equal(2, (await owner.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}/info")).GetProperty("musicCount").GetInt32());

        // The slideshow plays them in that order; choosing music doesn't make it need compiling again.
        var manifest = await (await owner.PostAsync($"/api/v1/albums/{album}/compile", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([b, a], manifest.GetProperty("music").EnumerateArray().Select(Id));
        (await owner.PutAsJsonAsync($"/api/v1/albums/{album}/music", new { trackIds = new[] { a } })).EnsureSuccessStatusCode();
        var get = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}");
        Assert.False(get.GetProperty("isStale").GetBoolean());
        Assert.Equal([a], get.GetProperty("music").EnumerateArray().Select(Id));

        // Share-link viewers get the same tracks and can play them without signing in.
        var token = (await (await owner.PutAsJsonAsync($"/api/v1/albums/{album}/share", new { enabled = true })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var anonymous = factory.CreateClient();
        var shared = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/shared/{token}");
        var music = Assert.Single(shared.GetProperty("music").EnumerateArray());
        Assert.Equal(a, Id(music));
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(music.GetProperty("url").GetString())).StatusCode);

        // Only library tracks, each once, at most 20.
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/v1/albums/{album}/music", new { trackIds = new[] { a, a } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/v1/albums/{album}/music", new { trackIds = new[] { Guid.NewGuid() } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/v1/albums/{album}/music", new { trackIds = Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()) })).StatusCode);

        // Someone else's trip is out of reach.
        var other = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/v1/albums/{album}/music", new { trackIds = new[] { b } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/albums/{album}/music")).StatusCode);
    }

    [Fact]
    public async Task Users_cannot_upload_music()
    {
        var owner = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        var album = await CreateAlbumAsync(owner, "No uploads");
        var res = await owner.PostAsync($"/api/v1/albums/{album}/images", TestMedia.Form(TestMedia.Mp3(), "song.mp3"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, res.StatusCode);
        Assert.Equal("music_not_uploadable", await AuthAndAccountTests.Code(res));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await owner.PostAsync($"/api/v1/albums/{album}/music", TestMedia.Form(TestMedia.Mp3(), "song.mp3"))).StatusCode);
    }

    [Fact]
    public async Task Videos_get_their_running_time_when_uploaded()
    {
        var owner = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        var album = await CreateAlbumAsync(owner, "Clips");
        var video = await UploadAsync(owner, album, TestMedia.Mp4(new DateTime(2026, 7, 2, 18, 0, 0, DateTimeKind.Utc), "+45.0703+007.6869/", durationMs: 12_500), "clip.mp4");
        Assert.Equal(12.5, video.GetProperty("durationSeconds").GetDouble());
        var photo = await UploadAsync(owner, album, TestMedia.Jpeg(), "p.jpg");
        Assert.Equal(JsonValueKind.Null, photo.GetProperty("durationSeconds").ValueKind);
    }

    [Fact]
    public async Task The_migration_deletes_uploaded_music_and_times_older_videos()
    {
        var store = factory.Services.GetRequiredService<ISlideshowStore>();
        var blobs = factory.Services.GetRequiredService<IBlobStore>();
        var owner = Guid.NewGuid().ToString();
        var album = new Album { Id = Guid.NewGuid(), OwnerId = owner, Name = "Before the library" };
        await store.AddAlbumAsync(album, default);

        MediaFile File(MediaCollection collection, MediaKind kind, string name)
        {
            var id = Guid.NewGuid();
            return new MediaFile
            {
                Id = id, AlbumId = album.Id, OwnerId = owner, Collection = collection, Kind = kind, FileName = name,
                ContentType = "application/octet-stream", BlobName = BlobNames.For(owner, album.Id, id),
            };
        }
        var song = File(MediaCollection.Music, MediaKind.Audio, "uploaded.mp3");
        var clip = File(MediaCollection.Images, MediaKind.Video, "old.mp4"); // uploaded before running times were read
        await blobs.UploadAsync(song.BlobName, new MemoryStream(TestMedia.Mp3()), "audio/mpeg", default);
        await blobs.UploadAsync(clip.BlobName, new MemoryStream(TestMedia.Mp4(durationMs: 61_000)), "video/mp4", default);
        await store.AddMediaAsync(song, default);
        await store.AddMediaAsync(clip, default);

        var (deleted, timed) = await LibraryMigration.RunAsync(store, blobs, NullLogger.Instance, default);
        Assert.True(deleted >= 1 && timed >= 1);
        Assert.Null(await store.GetMediaAsync(owner, album.Id, song.Id, default));
        Assert.Null(await blobs.OpenReadAsync(song.BlobName, default));
        Assert.Equal(61, (await store.GetMediaAsync(owner, album.Id, clip.Id, default))!.DurationSeconds);

        // Run again (the next start): nothing left to do for these.
        await LibraryMigration.RunAsync(store, blobs, NullLogger.Instance, default);
        Assert.Equal(61, (await store.GetMediaAsync(owner, album.Id, clip.Id, default))!.DurationSeconds);
    }
}
