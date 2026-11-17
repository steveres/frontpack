using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Slideshow.Api.Tests.Infrastructure;
using static Slideshow.Api.Tests.AlbumTests;

namespace Slideshow.Api.Tests;

/// <summary>Share links' preview pages (what LinkedIn, Messages etc. show for a link) and choosing a trip's cover.</summary>
public sealed class PreviewTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTime Day = new(2026, 7, 1, 9, 0, 0);

    private async Task<(HttpClient Owner, string Album, string First, string Second, string Hidden)> TripAsync(string name, string? description = null)
    {
        var owner = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        var album = await CreateAlbumAsync(owner, name, description);
        var first = await UploadAsync(owner, album, TestMedia.RealJpeg(640, 480, Day, (45.46, 9.19)), "first.jpg");
        var second = await UploadAsync(owner, album, TestMedia.RealJpeg(640, 480, Day.AddHours(1), (45.47, 9.20)), "second.jpg");
        var hidden = await UploadAsync(owner, album, TestMedia.RealJpeg(640, 480), "no-date-or-place.jpg"); // not in the slideshow
        return (owner, album, Id(first), Id(second), Id(hidden));
    }

    private static string Id(JsonElement media) => media.GetProperty("id").GetString()!;
    /// <summary>The cover image's address in a preview page: the cover's id (shortened) changes it.</summary>
    private static string CoverAddress(string token, string mediaId) => $"/{token}/cover.jpg?v={Guid.Parse(mediaId).ToString("N")[..12]}";

    private static async Task<string> ShareAsync(HttpClient owner, string album)
    {
        var res = await owner.PutAsJsonAsync($"/api/v1/albums/{album}/share", new { enabled = true });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> SetCoverAsync(HttpClient owner, string album, string? mediaId) =>
        owner.PutAsJsonAsync($"/api/v1/albums/{album}/cover", new { mediaId });

    [Fact]
    public async Task A_share_link_previews_the_trip_and_opens_its_slideshow()
    {
        var (owner, album, first, _, _) = await TripAsync("Milan in July", "Three days of walking.");
        var token = await ShareAsync(owner, album);
        var anonymous = factory.CreateClient();

        var res = await anonymous.GetAsync($"/{token}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/html", res.Content.Headers.ContentType!.MediaType);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("<meta property=\"og:title\" content=\"Milan in July\">", html);
        Assert.Contains("<meta property=\"og:description\" content=\"Three days of walking.\">", html);
        Assert.Contains(CoverAddress(token, first) + "\">", html);
        Assert.Contains($"/slideshow.html?share={token}\"", html); // where browsers go on to
        Assert.Contains("script-src 'sha256-", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("<script>location.replace(", html); // browsers go straight on…
        Assert.True(html.IndexOf("id=\"watch\"") < html.IndexOf("<script>"), "the script runs after the link it follows exists");

        // Apps previewing the link (Messages calls itself facebookexternalhit) get the tags without the redirect.
        var previewer = factory.CreateClient();
        previewer.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (iPhone) facebookexternalhit/1.1 Facebot Twitterbot/1.0");
        var preview = await previewer.GetStringAsync($"/{token}");
        Assert.DoesNotContain("<script", preview);
        Assert.Contains("<meta property=\"og:title\" content=\"Milan in July\">", preview);

        var cover = await anonymous.GetAsync($"/{token}/cover.jpg");
        Assert.Equal(HttpStatusCode.OK, cover.StatusCode);
        Assert.Equal("image/jpeg", cover.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task The_trips_name_is_text_in_the_page_never_markup()
    {
        var (owner, album, _, _, _) = await TripAsync("<script>alert(1)</script> \"Tom & Jerry\"");
        var token = await ShareAsync(owner, album);
        var html = await factory.CreateClient().GetStringAsync($"/{token}");
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("content=\"&lt;script&gt;alert(1)&lt;/script&gt; &quot;Tom &amp; Jerry&quot;\"", html);
    }

    [Fact]
    public async Task A_turned_off_or_wrong_link_previews_nothing_of_the_trip()
    {
        var (owner, album, _, _, _) = await TripAsync("Private now");
        var token = await ShareAsync(owner, album);
        (await owner.PutAsJsonAsync($"/api/v1/albums/{album}/share", new { enabled = false })).EnsureSuccessStatusCode();
        var anonymous = factory.CreateClient();

        foreach (var code in new[] { token, "AAAAAAAAAAAAAAAAAAAAAA", "not-a-code" })
        {
            var res = await anonymous.GetAsync($"/{code}");
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.DoesNotContain("Private now", await res.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/{code}/cover.jpg")).StatusCode);
        }
    }

    [Fact]
    public async Task The_owner_chooses_the_cover_from_the_slideshows_photos()
    {
        var (owner, album, first, second, hidden) = await TripAsync("Cover choice");
        var info = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}/info");
        Assert.Equal(first, info.GetProperty("coverId").GetString()); // automatically: the slideshow's first photo
        Assert.False(info.GetProperty("coverChosen").GetBoolean());

        var res = await SetCoverAsync(owner, album, second);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        info = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(second, info.GetProperty("coverId").GetString());
        Assert.True(info.GetProperty("coverChosen").GetBoolean());
        Assert.Contains($"/images/{second}/thumbnail", info.GetProperty("coverThumbnailUrl").GetString());

        // The link preview and the public listing use it too.
        var token = await ShareAsync(owner, album);
        (await owner.PutAsJsonAsync($"/api/v1/albums/{album}/share", new { isPublic = true })).EnsureSuccessStatusCode();
        Assert.Contains(CoverAddress(token, second) + "\">", await factory.CreateClient().GetStringAsync($"/{token}"));
        var listed = (await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/public-albums")).EnumerateArray()
            .Single(a => a.GetProperty("shareToken").GetString() == token);
        Assert.Contains(second, listed.GetProperty("coverThumbnailUrl").GetString());

        // Only a photo the slideshow shows; null goes back to choosing it automatically.
        Assert.Equal(HttpStatusCode.BadRequest, (await SetCoverAsync(owner, album, hidden)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetCoverAsync(owner, album, Guid.NewGuid().ToString())).StatusCode);
        info = await (await SetCoverAsync(owner, album, null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(first, info.GetProperty("coverId").GetString());
        Assert.False(info.GetProperty("coverChosen").GetBoolean());
    }

    [Fact]
    public async Task Deleting_the_chosen_cover_goes_back_to_the_automatic_one()
    {
        var (owner, album, first, second, _) = await TripAsync("Cover deleted");
        (await SetCoverAsync(owner, album, second)).EnsureSuccessStatusCode();
        (await owner.DeleteAsync($"/api/v1/albums/{album}/images/{second}")).EnsureSuccessStatusCode();
        var info = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/albums/{album}/info");
        Assert.Equal(first, info.GetProperty("coverId").GetString());
        Assert.False(info.GetProperty("coverChosen").GetBoolean());
    }

    [Fact]
    public async Task Only_the_owner_chooses_the_cover()
    {
        var (_, album, first, _, _) = await TripAsync("Someone else's");
        var stranger = await factory.RegisteredClientAsync(Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await SetCoverAsync(stranger, album, first)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SetCoverAsync(factory.CreateClient(), album, first)).StatusCode);
    }
}
