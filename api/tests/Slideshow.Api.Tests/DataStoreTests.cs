using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Slideshow.Api.Data;
using Slideshow.Api.Tests.Infrastructure;
using static Slideshow.Api.Tests.AlbumTests;

namespace Slideshow.Api.Tests;

/// <summary>
/// What SQL transactions used to guarantee, now that the data is in tables: concurrent changes all
/// count, and deletes leave nothing behind. Runs against memory, or Azurite with SLIDESHOW_TEST_TABLES=azurite.
/// </summary>
public sealed class DataStoreTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private IDataStore Store => factory.Services.GetRequiredService<IDataStore>();

    [Fact]
    public async Task Concurrent_album_updates_are_all_kept()
    {
        var userId = Guid.NewGuid().ToString();
        var client = await factory.RegisteredClientAsync(userId);
        var album = Guid.Parse(await CreateAlbumAsync(client, "Busy"));

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Store.BumpVersionAsync(userId, album, DateTime.UtcNow, CancellationToken.None)));
        Assert.Equal(20, (await Store.GetAlbumAsync(userId, album, CancellationToken.None))!.ContentVersion);
    }

    [Fact]
    public async Task Concurrent_uploads_all_land_and_mark_the_album_changed()
    {
        var client = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        var album = await CreateAlbumAsync(client, "Many at once");
        await client.PostAsync($"/api/v1/albums/{album}/compile", null);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            UploadAsync(client, album, TestMedia.Jpeg(new DateTime(2026, 7, 1, 9, i, 0), (45.0, 9.0)), $"p{i}.jpg")));

        var info = await client.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}/info");
        Assert.Equal(8, info.GetProperty("imageCount").GetInt32());
        Assert.True(info.GetProperty("isStale").GetBoolean());
        var manifest = await (await client.PostAsync($"/api/v1/albums/{album}/compile", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(8, manifest.GetProperty("slides").GetArrayLength());
        Assert.False((await client.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}/info")).GetProperty("isStale").GetBoolean());
    }

    [Fact]
    public async Task Deleting_the_account_leaves_no_rows_or_share_codes()
    {
        var userId = Guid.NewGuid().ToString();
        var client = await factory.RegisteredClientAsync(userId);
        var album = await CreateAlbumAsync(client, "Gone soon");
        await UploadAsync(client, album, TestMedia.Jpeg(new DateTime(2026, 7, 1, 9, 0, 0), (45.0, 9.0)), "a.jpg");
        var share = await (await client.PutAsJsonAsync($"/api/v1/albums/{album}/share", new { enabled = true })).Content.ReadFromJsonAsync<JsonElement>();
        var token = share.GetProperty("token").GetString()!;
        Assert.NotNull(await Store.ResolveShareAsync(token, CancellationToken.None));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/account")).StatusCode);

        var ct = CancellationToken.None;
        Assert.Null(await Store.GetUserAsync(userId, ct));
        Assert.Empty(await Store.ListAlbumsAsync(userId, ct));
        Assert.Empty(await Store.ListMediaAsync(userId, null, ct));
        Assert.Null(await Store.ResolveShareAsync(token, ct));
    }

    [Fact]
    public async Task Share_codes_follow_the_album()
    {
        var userId = Guid.NewGuid().ToString();
        var client = await factory.RegisteredClientAsync(userId);
        var album = await CreateAlbumAsync(client, "Shared");
        async Task<string?> ShareAsync(object body) =>
            (await (await client.PutAsJsonAsync($"/api/v1/albums/{album}/share", body)).Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("token").GetString();

        var first = (await ShareAsync(new { enabled = true }))!;
        var second = (await (await client.PostAsync($"/api/v1/albums/{album}/share/reset", null)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("token").GetString()!;
        var ct = CancellationToken.None;
        Assert.Null(await Store.ResolveShareAsync(first, ct));                         // the old code is retired
        Assert.Equal((userId, Guid.Parse(album)), await Store.ResolveShareAsync(second, ct));

        Assert.Null(await ShareAsync(new { enabled = false }));
        Assert.Null(await Store.ResolveShareAsync(second, ct));

        // Registration can't claim a taken id, and a share code can't be claimed twice.
        Assert.False(await Store.TryAddUserAsync(new User { Id = userId, CreatedAt = DateTime.UtcNow }, ct));
        Assert.True(await Store.TryAddShareAsync("abcdefghijklmnopqrstuv", userId, Guid.Parse(album), ct));
        Assert.False(await Store.TryAddShareAsync("abcdefghijklmnopqrstuv", userId, Guid.Parse(album), ct));
        await Store.DeleteShareAsync("abcdefghijklmnopqrstuv", ct);
    }

    [Fact]
    public async Task Fields_survive_a_round_trip()
    {
        var userId = Guid.NewGuid().ToString();
        var client = await factory.RegisteredClientAsync(userId);
        var album = await CreateAlbumAsync(client, "Round trip", "with a description");
        var taken = new DateTime(2026, 7, 4, 9, 31, 5);
        var info = await UploadAsync(client, album, TestMedia.Jpeg(taken, (45.4642, 9.19)), "milan.jpg");

        var media = await Store.GetMediaAsync(userId, Guid.Parse(album), Guid.Parse(info.GetProperty("id").GetString()!), CancellationToken.None);
        Assert.NotNull(media);
        Assert.Equal(taken, media.TakenAt);                         // local wall time, not shifted
        Assert.Equal(DateTimeKind.Unspecified, media.TakenAt!.Value.Kind);
        Assert.Equal(45.4642, media.Latitude!.Value, 4);
        Assert.Equal(TakenSource.Exif, media.TakenSource);
        Assert.Equal(DateTimeKind.Utc, media.UploadedAt.Kind);
        var stored = await Store.GetAlbumAsync(userId, Guid.Parse(album), CancellationToken.None);
        Assert.Equal("with a description", stored!.Description);
        Assert.Equal(1, stored.ContentVersion);
    }
}
