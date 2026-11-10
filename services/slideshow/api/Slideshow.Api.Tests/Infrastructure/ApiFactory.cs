using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using FrontPack.Api;
using FrontPack.Hosts;

namespace Slideshow.Api.Tests.Infrastructure;

/// <summary>
/// FrontPack's and Slideshow's APIs, as deployed: two real hosts behind one client, which sends the account
/// and development sign-in calls to FrontPack and the rest to Slideshow. They share the accounts, and FrontPack
/// reaches Slideshow's side of the account on its internal endpoints. Most tests run against this.
/// </summary>
public sealed class ApiFactory : IDisposable
{
    private readonly Lazy<(HostFactory<FrontPackHost> FrontPack, HostFactory<SlideshowHost> Slideshow)> _hosts;

    public ApiFactory() => _hosts = new(Start);

    /// <summary>Configuration overrides for both APIs, e.g. smaller limits.</summary>
    public Dictionary<string, string?> Settings { get; init; } = [];
    /// <summary>Hosting environment of both (see <see cref="HostFactory{THost}.Environment"/>).</summary>
    public string Environment { get; init; } = "Testing";
    /// <summary>More service changes for FrontPack's API (e.g. another service's side of the account).</summary>
    public Action<IServiceCollection>? FrontPackServices { get; init; }

    public HostFactory<FrontPackHost> FrontPack => _hosts.Value.FrontPack;
    public HostFactory<SlideshowHost> Slideshow => _hosts.Value.Slideshow;
    /// <summary>Slideshow's services (its stores, queue and files; the accounts are the same in both).</summary>
    public IServiceProvider Services => Slideshow.Services;
    public string MediaRoot => Slideshow.MediaRoot;
    public FakeIdentityDirectory Directory => FrontPack.Directory;

    private (HostFactory<FrontPackHost>, HostFactory<SlideshowHost>) Start()
    {
        // One accounts store for both (in Azure: one Users table); Azurite runs share theirs already.
        var azurite = System.Environment.GetEnvironmentVariable("SLIDESHOW_TEST_TABLES") == "azurite";
        var users = new LocalUserStore(Microsoft.Extensions.Options.Options.Create(new UsersOptions { LocalPath = "" }));
        void SharedUsers(IServiceCollection s) { if (!azurite) s.AddSingleton<IUserStore>(users); }

        // In a test server every request's local port is 0, so Internal:Port 0 lets FrontPack's calls through.
        var slideshow = new HostFactory<SlideshowHost>
        {
            Environment = Environment,
            Settings = new(Settings) { ["Internal:Port"] = "0" },
            TestServices = SharedUsers,
        };
        var frontpack = new HostFactory<FrontPackHost>
        {
            Environment = Environment,
            Settings = new(Settings) { ["Services:0:Name"] = "slideshow", ["Services:0:InternalUrl"] = "http://slideshow.internal:8081" },
            TestServices = s =>
            {
                SharedUsers(s);
                s.AddHttpClient(RemoteServiceAccountData.ClientName("slideshow")).ConfigurePrimaryHttpMessageHandler(() => slideshow.Server.CreateHandler());
                FrontPackServices?.Invoke(s);
            },
        };
        return (frontpack, slideshow);
    }

    /// <summary>A client for both APIs: each request goes to the one that serves its path.</summary>
    public HttpClient CreateClient() =>
        new(new RouteToApi(FrontPack.Server.CreateHandler(), Slideshow.Server.CreateHandler())) { BaseAddress = new Uri("http://localhost/") };

    /// <summary>A client acting as the given user.</summary>
    public HttpClient ClientFor(string oid, string? token = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token ?? Token(oid));
        return client;
    }

    /// <summary>A client for a user who has already registered.</summary>
    public async Task<HttpClient> RegisteredClientAsync(string oid)
    {
        var client = ClientFor(oid);
        (await client.PostAsync("/api/v1/account", null)).EnsureSuccessStatusCode();
        return client;
    }

    /// <inheritdoc cref="HostFactory{THost}.Token"/>
    public static string Token(string oid, string? scope = HostFactory<SlideshowHost>.Scope, string audience = HostFactory<SlideshowHost>.Audience,
        DateTime? issuedAt = null, SecurityKey? key = null, string name = "Test User", string[]? roles = null) =>
        HostFactory<SlideshowHost>.Token(oid, scope, audience, issuedAt, key, name, roles);

    /// <summary>A registered user with the Music.Admin app role (as Entra gives it).</summary>
    public async Task<HttpClient> MusicAdminClientAsync()
    {
        var oid = Guid.NewGuid().ToString();
        var client = ClientFor(oid, Token(oid, roles: ["Music.Admin"]));
        (await client.PostAsync("/api/v1/account", null)).EnsureSuccessStatusCode();
        return client;
    }

    public static TimeSpan EntraBackdating => HostFactory<SlideshowHost>.EntraBackdating;

    public void Dispose()
    {
        if (!_hosts.IsValueCreated) return;
        _hosts.Value.FrontPack.Dispose();
        _hosts.Value.Slideshow.Dispose();
    }

    /// <summary>FrontPack serves the account and development sign-in; Slideshow everything else.</summary>
    private sealed class RouteToApi(HttpMessageHandler frontpack, HttpMessageHandler slideshow) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _frontpack = new(frontpack), _slideshow = new(slideshow);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var toFrontPack = path.StartsWith("/api/v1/account", StringComparison.Ordinal) || path.StartsWith("/dev/", StringComparison.Ordinal);
            return (toFrontPack ? _frontpack : _slideshow).SendAsync(request, ct);
        }
    }
}

/// <summary>
/// A real host with its data in memory + a temp folder for storage, and tokens signed by a test key in place of
/// Entra's. Everything else (JWT validation, scope check, logout cut-off) is the production pipeline.
/// </summary>
public class HostFactory<THost> : WebApplicationFactory<THost> where THost : class
{
    public const string Issuer = "https://login.test/tenant-id/v2.0";
    public const string Audience = "api-client-id";
    public const string Scope = "FrontPack.Access";
    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("test-signing-key-that-is-long-enough-for-hs256!"));

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "slideshow-tests", Guid.NewGuid().ToString("N"));
    public string MediaRoot => Path.Combine(Root, "media");
    public FakeIdentityDirectory Directory { get; } = new();

    /// <summary>Configuration overrides, e.g. smaller limits.</summary>
    public Dictionary<string, string?> Settings { get; init; } = [];

    /// <summary>Hosting environment. In "Development" the test-key token setup is skipped, so the app's own
    /// Development token configuration (user-jwts style) applies.</summary>
    public string Environment { get; init; } = "Testing";

    /// <summary>More service changes, applied last (e.g. wiring FrontPack's API to a Slideshow test server).</summary>
    public Action<IServiceCollection>? TestServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        var testTokens = Environment != "Development";
        if (System.Environment.GetEnvironmentVariable("SLIDESHOW_TEST_TABLES") == "azurite")
        {
            // Same tests against the Table Storage and queue code: start Azurite first (see api/README.md).
            builder.UseSetting("Users:Provider", "AzureTable");
            builder.UseSetting("Users:ConnectionString", "UseDevelopmentStorage=true");
            builder.UseSetting("Tables:Provider", "AzureTable");
            builder.UseSetting("Tables:ConnectionString", "UseDevelopmentStorage=true");
            builder.UseSetting("Resize:Provider", "AzureQueue");
            builder.UseSetting("Resize:ConnectionString", "UseDevelopmentStorage=true");
            builder.UseSetting("Resize:QueueName", "resize-" + Guid.NewGuid().ToString("N")); // each test app has its own files, so its own queue
        }
        else
        {
            builder.UseSetting("Users:Provider", "Local");
            builder.UseSetting("Users:LocalPath", ""); // memory only
            builder.UseSetting("Tables:Provider", "Local");
            builder.UseSetting("Tables:LocalPath", ""); // memory only
        }
        builder.UseSetting("Storage:Provider", "FileSystem");
        builder.UseSetting("Storage:RootPath", MediaRoot);
        if (testTokens)
        {
            builder.UseSetting("Auth:Authority", "https://login.test/tenant-id/v2.0");
            builder.UseSetting("Auth:Audiences:0", Audience);
        }
        else
        {
            builder.UseSetting("Auth:Authority", ""); // Development tests use only user-jwts tokens: never contact the real tenant
        }
        builder.UseSetting("Auth:RequiredScope", Scope);
        builder.UseSetting("Cors:AllowedOrigins:0", "https://spa.test");
        foreach (var (k, v) in Settings) builder.UseSetting(k, v);

        builder.ConfigureTestServices(services =>
        {
            if (testTokens)
            {
                services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    var config = new OpenIdConnectConfiguration { Issuer = Issuer };
                    config.SigningKeys.Add(Key);
                    o.Configuration = config; // no metadata download
                });
            }
            services.AddScoped<IIdentityDirectory>(_ => Directory);
            TestServices?.Invoke(services);
        });
    }

    /// <summary>Like Entra: a token's "iat" and "nbf" are stamped this long before it's really issued.</summary>
    public static readonly TimeSpan EntraBackdating = TimeSpan.FromMinutes(5);

    /// <param name="issuedAt">When the token is really issued (default now); it's stamped <see cref="EntraBackdating"/> earlier.</param>
    /// <param name="roles">App roles, in the "roles" claim as Entra lists them.</param>
    public static string Token(string oid, string? scope = Scope, string audience = Audience, DateTime? issuedAt = null,
        SecurityKey? key = null, string name = "Test User", string[]? roles = null)
    {
        var iat = (issuedAt ?? DateTime.UtcNow) - EntraBackdating;
        var claims = new Dictionary<string, object> { ["oid"] = oid, ["name"] = name, ["email"] = $"{oid}@example.test" };
        if (scope is not null) claims["scp"] = scope;
        if (roles is not null) claims["roles"] = roles;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = iat,
            NotBefore = iat,
            Expires = iat + EntraBackdating + TimeSpan.FromHours(1),
            SigningCredentials = new SigningCredentials(key ?? Key, SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>A client acting as the given user.</summary>
    public HttpClient ClientFor(string oid, string? token = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token ?? Token(oid));
        return client;
    }

    /// <summary>A client for a user who has already registered.</summary>
    public async Task<HttpClient> RegisteredClientAsync(string oid)
    {
        var client = ClientFor(oid);
        (await client.PostAsync("/api/v1/account", null)).EnsureSuccessStatusCode();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { System.IO.Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

public sealed class FakeIdentityDirectory : IIdentityDirectory
{
    public ConcurrentQueue<string> Revoked { get; } = new();
    public ConcurrentQueue<string> Deleted { get; } = new();
    public bool FailDeletes { get; set; }

    public Task RevokeSessionsAsync(string userId, CancellationToken ct) { Revoked.Enqueue(userId); return Task.CompletedTask; }

    public Task DeleteUserAsync(string userId, CancellationToken ct)
    {
        if (FailDeletes) throw new HttpRequestException("Graph unavailable");
        Deleted.Enqueue(userId);
        return Task.CompletedTask;
    }
}
