using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FrontPack.Api;
using FrontPack.Hosts;
using Microsoft.Extensions.DependencyInjection;
using Slideshow.Api.Data;
using Slideshow.Api.Tests.Infrastructure;

namespace Slideshow.Api.Tests;

/// <summary>
/// FrontPack and Slideshow as separate APIs, as they will run in Azure: they share the accounts (here, one
/// local file), and FrontPack's API reaches Slideshow's side of the account on Slideshow's internal port.
/// </summary>
public sealed class SeparateHostsTests : IDisposable
{
    private readonly string _users = Path.Combine(Path.GetTempPath(), "slideshow-tests", Guid.NewGuid().ToString("N"), "users.json");
    private readonly HostFactory<SlideshowHost> _slideshow;
    private readonly HostFactory<FrontPackHost> _frontpack;

    public SeparateHostsTests()
    {
        // In a test server every request's local port is 0, so Internal:Port 0 lets FrontPack's calls through.
        _slideshow = new() { Settings = { ["Users:LocalPath"] = _users, ["Internal:Port"] = "0" } };
        _frontpack = new()
        {
            Settings =
            {
                ["Users:LocalPath"] = _users,
                ["Services:0:Name"] = "slideshow",
                ["Services:0:InternalUrl"] = "http://slideshow.internal:8081",
            },
            TestServices = s => s.AddHttpClient(RemoteServiceAccountData.ClientName("slideshow"))
                .ConfigurePrimaryHttpMessageHandler(() => _slideshow.Server.CreateHandler()),
        };
    }

    private static HttpClient As(string oid, HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token(oid));
        return client;
    }

    [Fact]
    public async Task Account_on_FrontPack_covers_the_data_in_Slideshow()
    {
        var oid = Guid.NewGuid().ToString();
        var frontpack = As(oid, _frontpack.CreateClient());
        var slideshow = As(oid, _slideshow.CreateClient());

        // Registered with FrontPack, the user can use Slideshow straight away.
        (await frontpack.PostAsync("/api/v1/account", null)).EnsureSuccessStatusCode();
        var album = await AlbumTests.CreateAlbumAsync(slideshow, "Separate");
        (await slideshow.PostAsync($"/api/v1/albums/{album}/images", TestMedia.Form(TestMedia.Jpeg(), "a.jpg"))).EnsureSuccessStatusCode();

        var usage = (await frontpack.GetFromJsonAsync<JsonElement>("/api/v1/account")).GetProperty("usage");
        Assert.Equal(1, usage.GetProperty("albums").GetInt32());
        Assert.Equal(1, usage.GetProperty("files").GetInt32());
        Assert.Equal(TestMedia.Jpeg().Length, usage.GetProperty("bytes").GetInt64());

        // Deleting the account on FrontPack deletes the albums in Slideshow, and Slideshow stops accepting the user.
        Assert.Equal(HttpStatusCode.NoContent, (await frontpack.DeleteAsync("/api/v1/account")).StatusCode);
        Assert.Empty(await _slideshow.Services.GetRequiredService<ISlideshowStore>().ListAlbumsAsync(oid, CancellationToken.None));
        Assert.Equal(HttpStatusCode.Forbidden, (await slideshow.GetAsync("/api/v1/albums")).StatusCode);
    }

    [Fact]
    public async Task Signing_out_on_FrontPack_also_signs_out_of_Slideshow()
    {
        var oid = Guid.NewGuid().ToString();
        var frontpack = As(oid, _frontpack.CreateClient());
        var slideshow = As(oid, _slideshow.CreateClient());
        (await frontpack.PostAsync("/api/v1/account", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await slideshow.GetAsync("/api/v1/albums")).StatusCode);

        // The cut-off is at least the token's backdating ahead of the token's (stamped) issue time.
        await Task.Delay(TimeSpan.FromMilliseconds(10));
        Assert.Equal(HttpStatusCode.NoContent, (await frontpack.PostAsync("/api/v1/account/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await slideshow.GetAsync("/api/v1/albums")).StatusCode);
    }

    [Fact]
    public async Task Internal_endpoints_answer_only_on_the_internal_port()
    {
        using var slideshow = new HostFactory<SlideshowHost> { Settings = { ["Internal:Port"] = "8081" } };
        var res = await slideshow.CreateClient().GetAsync($"/internal/users/{Guid.NewGuid()}/usage");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    public void Dispose()
    {
        _frontpack.Dispose();
        _slideshow.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(_users)!, recursive: true); } catch (IOException) { }
    }
}
