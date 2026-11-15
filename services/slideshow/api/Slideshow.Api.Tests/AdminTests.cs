using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Slideshow.Api.Data;
using Slideshow.Api.Tests.Infrastructure;
using static Slideshow.Api.Tests.AlbumTests;

namespace Slideshow.Api.Tests;

/// <summary>The site admins' overview: everyone's users and trips, and any trip's slideshow, for site admins only.</summary>
public sealed class AdminTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private ISlideshowStore Store => factory.Services.GetRequiredService<ISlideshowStore>();

    /// <summary>A user with a trip of two photos: one in the slideshow, one left out (no date or place).</summary>
    private async Task<(string OwnerId, HttpClient Owner, string Album, JsonElement Shown, JsonElement Hidden)> TripAsync(string name = "Admin's look")
    {
        var ownerId = Guid.NewGuid().ToString();
        var owner = await factory.RegisteredClientAsync(ownerId);
        var album = await CreateAlbumAsync(owner, name);
        var shown = await UploadAsync(owner, album, TestMedia.Jpeg(new DateTime(2026, 7, 1, 9, 0, 0), (45.4642, 9.19), padding: 1000), "milan.jpg");
        var hidden = await UploadAsync(owner, album, TestMedia.Jpeg(padding: 500), "no-date-or-place.jpg");
        return (ownerId, owner, album, shown, hidden);
    }

    private static string Id(JsonElement e) => e.GetProperty("id").GetString()!;

    private static JsonElement Find(JsonElement overview, string list, string id) =>
        overview.GetProperty(list).EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);

    [Fact]
    public async Task Only_site_admins_get_in()
    {
        var (ownerId, owner, album, shown, _) = await TripAsync();
        var musicAdmin = await factory.MusicAdminClientAsync();
        var anonymous = factory.CreateClient();
        string[] paths = ["/api/v1/admin/access", "/api/v1/admin/overview", $"/api/v1/admin/trips/{ownerId}/{album}", $"/api/v1/admin/trips/{ownerId}/{album}/images/{Id(shown)}"];
        foreach (var path in paths)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync(path)).StatusCode);      // even for their own trip
            Assert.Equal(HttpStatusCode.Forbidden, (await musicAdmin.GetAsync(path)).StatusCode); // a different role
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        }
        var admin = await factory.SiteAdminClientAsync();
        var res = await admin.GetAsync("/api/v1/admin/access");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("no-store", res.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task The_overview_counts_each_users_trips_files_and_storage()
    {
        var (ownerId, owner, album, shown, hidden) = await TripAsync("Counted");
        await CreateAlbumAsync(owner, "Empty one");
        var bytes = shown.GetProperty("sizeBytes").GetInt64() + hidden.GetProperty("sizeBytes").GetInt64();
        var admin = await factory.SiteAdminClientAsync();

        var overview = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/overview");
        var user = Find(overview, "users", ownerId);
        Assert.Equal("Test User", user.GetProperty("name").GetString());
        Assert.Equal($"{ownerId}@example.test", user.GetProperty("email").GetString());
        Assert.Equal(2, user.GetProperty("tripCount").GetInt32());
        Assert.Equal(2, user.GetProperty("photoCount").GetInt32());
        Assert.Equal(bytes, user.GetProperty("bytes").GetInt64());
        Assert.NotEqual(JsonValueKind.Null, user.GetProperty("lastSeenAt").ValueKind); // they've used Trip Pix

        var trip = Find(overview, "trips", album);
        Assert.Equal("Counted", trip.GetProperty("name").GetString());
        Assert.Equal(ownerId, trip.GetProperty("ownerId").GetString());
        Assert.Equal($"{ownerId}@example.test", trip.GetProperty("ownerEmail").GetString());
        Assert.Equal(2, trip.GetProperty("photoCount").GetInt32());
        Assert.Equal(1, trip.GetProperty("excludedCount").GetInt32());
        Assert.Equal(bytes, trip.GetProperty("bytes").GetInt64());
        Assert.False(trip.GetProperty("isShared").GetBoolean());
        Assert.Equal(0, trip.GetProperty("views").GetInt64());

        var totals = overview.GetProperty("totals");
        Assert.True(totals.GetProperty("users").GetInt32() >= 2); // the owner and the admin (other tests add more)
        Assert.True(totals.GetProperty("bytes").GetInt64() >= bytes);
        Assert.True(overview.GetProperty("quotaBytesPerUser").GetInt64() > 0);
    }

    [Fact]
    public async Task Opening_the_share_link_counts_as_a_view_but_the_owner_playing_it_does_not()
    {
        var (_, owner, album, _, _) = await TripAsync();
        (await owner.PostAsync($"/api/v1/albums/{album}/compile", null)).EnsureSuccessStatusCode();
        (await owner.GetAsync($"/api/v1/albums/{album}")).EnsureSuccessStatusCode(); // the owner plays it
        var share = await (await owner.PutAsJsonAsync($"/api/v1/albums/{album}/share", new { enabled = true })).Content.ReadFromJsonAsync<JsonElement>();
        var token = share.GetProperty("token").GetString()!;
        var anonymous = factory.CreateClient();
        for (var i = 0; i < 3; i++) (await anonymous.GetAsync($"/api/v1/shared/{token}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/v1/shared/AAAAAAAAAAAAAAAAAAAAAA")).StatusCode); // a wrong link counts nothing

        var trip = Find(await (await factory.SiteAdminClientAsync()).GetFromJsonAsync<JsonElement>("/api/v1/admin/overview"), "trips", album);
        Assert.Equal(3, trip.GetProperty("views").GetInt64());
        Assert.NotEqual(JsonValueKind.Null, trip.GetProperty("lastViewedAt").ValueKind);
        Assert.True(trip.GetProperty("isShared").GetBoolean());
    }

    [Fact]
    public async Task A_site_admin_can_play_anyones_trip_without_changing_it()
    {
        var (ownerId, owner, album, shown, hidden) = await TripAsync("Private trip");
        var before = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}/info");
        var admin = await factory.SiteAdminClientAsync();

        var res = await admin.GetAsync($"/api/v1/admin/trips/{ownerId}/{album}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("no-store", res.Headers.CacheControl!.ToString());
        var trip = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Private trip", trip.GetProperty("name").GetString());
        Assert.Equal($"{ownerId}@example.test", trip.GetProperty("ownerEmail").GetString());
        Assert.Equal(1, trip.GetProperty("excludedCount").GetInt32());
        var slide = Assert.Single(trip.GetProperty("slides").EnumerateArray());
        Assert.Equal($"/api/v1/admin/trips/{ownerId}/{album}/images/{Id(shown)}", slide.GetProperty("url").GetString());

        var file = await admin.GetAsync(slide.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Contains("no-store", file.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/admin/trips/{ownerId}/{album}/images/{Id(hidden)}")).StatusCode);

        // Not compiled, not shared, not counted: the owner's trip is as it was.
        var after = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}/info");
        Assert.Equal(before.GetProperty("compiledAt").ToString(), after.GetProperty("compiledAt").ToString());
        Assert.Equal(before.GetProperty("updatedAt").GetString(), after.GetProperty("updatedAt").GetString());
        Assert.False(after.GetProperty("isShared").GetBoolean());
        Assert.Equal(0, Find(await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/overview"), "trips", album).GetProperty("views").GetInt64());
    }

    [Fact]
    public async Task Wrong_owner_or_trip_is_not_found()
    {
        var (ownerId, _, album, shown, _) = await TripAsync();
        var admin = await factory.SiteAdminClientAsync();
        var stranger = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/admin/trips/{stranger}/{album}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/admin/trips/{ownerId}/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/admin/trips/{stranger}/{album}/images/{Id(shown)}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/v1/admin/trips/{ownerId}/{album}/images/{Id(shown)}?size=5")).StatusCode);
    }

    [Fact]
    public async Task Last_seen_is_written_at_most_hourly()
    {
        var ownerId = Guid.NewGuid().ToString();
        var owner = await factory.RegisteredClientAsync(ownerId);
        (await owner.GetAsync("/api/v1/albums")).EnsureSuccessStatusCode();
        var first = (await Store.ReadActivityAsync(default)).LastSeen[ownerId];
        Assert.True(DateTime.UtcNow - first < TimeSpan.FromMinutes(1));

        await Task.Delay(20);
        (await owner.GetAsync("/api/v1/albums")).EnsureSuccessStatusCode();
        Assert.Equal(first, (await Store.ReadActivityAsync(default)).LastSeen[ownerId]); // not written again within the hour
    }

    [Fact]
    public async Task Deleting_a_trip_or_the_account_forgets_its_activity()
    {
        var (ownerId, owner, album, _, _) = await TripAsync();
        var other = await CreateAlbumAsync(owner, "Second");
        await Store.RecordViewAsync(ownerId, Guid.Parse(album), DateTime.UtcNow, default);
        await Store.RecordViewAsync(ownerId, Guid.Parse(other), DateTime.UtcNow, default);

        (await owner.DeleteAsync($"/api/v1/albums/{album}")).EnsureSuccessStatusCode();
        var activity = await Store.ReadActivityAsync(default);
        Assert.False(activity.Views.ContainsKey((ownerId, Guid.Parse(album))));
        Assert.True(activity.Views.ContainsKey((ownerId, Guid.Parse(other))));

        (await owner.DeleteAsync("/api/v1/account")).EnsureSuccessStatusCode();
        activity = await Store.ReadActivityAsync(default);
        Assert.False(activity.LastSeen.ContainsKey(ownerId));
        Assert.False(activity.Views.ContainsKey((ownerId, Guid.Parse(other))));
    }
}
