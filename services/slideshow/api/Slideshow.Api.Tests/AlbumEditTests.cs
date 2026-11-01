using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Slideshow.Api.Tests.Infrastructure;
using static Slideshow.Api.Tests.AlbumTests;

namespace Slideshow.Api.Tests;

/// <summary>Renaming an album, and deleting several files at once.</summary>
public sealed class AlbumEditTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, string album, object body) =>
        client.PatchAsJsonAsync($"/api/v1/albums/{album}", body);

    [Fact]
    public async Task Rename_and_change_the_description()
    {
        var client = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        var album = await CreateAlbumAsync(client, "Italy", "Summer");

        var res = await PatchAsync(client, album, new { name = "  Italy 2026  ", description = "Two weeks in July" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var info = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Italy 2026", info.GetProperty("name").GetString());
        Assert.Equal("Two weeks in July", info.GetProperty("description").GetString());

        // Only the name: the description stays. An empty description removes it.
        await PatchAsync(client, album, new { name = "Italia" });
        info = await client.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}/info");
        Assert.Equal("Italia", info.GetProperty("name").GetString());
        Assert.Equal("Two weeks in July", info.GetProperty("description").GetString());
        await PatchAsync(client, album, new { description = "  " });
        info = await client.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}/info");
        Assert.Equal(JsonValueKind.Null, info.GetProperty("description").ValueKind);
        Assert.Equal("Italia", info.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Renaming_shows_in_the_slideshow_without_recompiling()
    {
        var client = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        var album = await CreateAlbumAsync(client, "Old name");
        await UploadAsync(client, album, TestMedia.Jpeg(new DateTime(2026, 7, 1, 9, 0, 0), (45.0, 9.0)), "a.jpg");
        await client.PostAsync($"/api/v1/albums/{album}/compile", null);

        await PatchAsync(client, album, new { name = "New name" });
        var manifest = await client.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}");
        Assert.Equal("New name", manifest.GetProperty("name").GetString());
        Assert.False(manifest.GetProperty("isStale").GetBoolean());
    }

    [Fact]
    public async Task Rename_is_validated_and_owner_only()
    {
        var client = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        var album = await CreateAlbumAsync(client, "Mine");
        Assert.Equal(HttpStatusCode.BadRequest, (await PatchAsync(client, album, new { name = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PatchAsync(client, album, new { name = new string('x', 201) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PatchAsync(client, album, new { description = new string('x', 2001) })).StatusCode);

        var other = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await PatchAsync(other, album, new { name = "Theirs now" })).StatusCode);
        Assert.Equal("Mine", (await client.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}/info")).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Delete_several_files_at_once()
    {
        var client = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        var album = await CreateAlbumAsync(client, "Many");
        var photos = new List<string>();
        for (var i = 0; i < 5; i++)
            photos.Add((await UploadAsync(client, album, TestMedia.RealJpeg(200, 200, new DateTime(2026, 7, 1, 9, i, 0), (45.0, 9.0)), $"p{i}.jpg"))
                .GetProperty("id").GetString()!);
        var track = (await UploadAsync(client, album, TestMedia.Mp3(), "song.mp3", "music")).GetProperty("id").GetString()!;
        await client.PostAsync($"/api/v1/albums/{album}/compile", null);

        // Three photos, plus ids that aren't images in this album (the track, an unknown id): those are skipped.
        var res = await client.PostAsJsonAsync($"/api/v1/albums/{album}/images/delete",
            new { ids = new[] { photos[0], photos[2], photos[4], track, Guid.NewGuid().ToString() } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(3, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("deleted").GetInt32());

        var left = await client.GetFromJsonAsync<JsonElement[]>($"/api/v1/albums/{album}/images");
        Assert.Equal([photos[1], photos[3]], left!.Select(m => m.GetProperty("id").GetString()).Order());
        Assert.Single((await client.GetFromJsonAsync<JsonElement[]>($"/api/v1/albums/{album}/music"))!);
        Assert.True((await client.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}/info")).GetProperty("isStale").GetBoolean());

        // The files and their previews are gone too.
        var albumFolder = Directory.GetDirectories(factory.MediaRoot, Guid.Parse(album).ToString("N"), SearchOption.AllDirectories).Single();
        foreach (var id in new[] { photos[0], photos[2], photos[4] })
            Assert.Empty(Directory.GetFiles(albumFolder, Guid.Parse(id).ToString("N") + "*"));
        Assert.NotEmpty(Directory.GetFiles(albumFolder, Guid.Parse(photos[1]).ToString("N") + "*"));
    }

    [Fact]
    public async Task Bulk_delete_is_validated_and_owner_only()
    {
        var client = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        var album = await CreateAlbumAsync(client, "Mine");
        var photo = (await UploadAsync(client, album, TestMedia.Jpeg(), "a.jpg")).GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/albums/{album}/images/delete", new { ids = Array.Empty<string>() })).StatusCode);

        var other = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/albums/{album}/images/delete", new { ids = new[] { photo } })).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<JsonElement[]>($"/api/v1/albums/{album}/images"))!);
    }
}
