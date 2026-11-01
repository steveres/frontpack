// FrontPack and Slideshow in one API, deployed as today's slideshow-api container app. Each service also has a
// host of its own (FrontPack.Host, Slideshow.Host); this one goes once traffic has moved to them.
using Azure.Core;
using Azure.Identity;
using FrontPack.Api;
using Slideshow.Api;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

// Signing in to Table, Blob and Queue Storage in Azure: the API's managed identity, straight away (no credential
// chain), and one credential for all of them so they share one storage token. AZURE_CLIENT_ID picks the identity.
services.AddSingleton<TokenCredential>(_ => builder.Configuration["AZURE_CLIENT_ID"] is { Length: > 0 } clientId
    ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
    : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));

// Sign-in, the FrontPack accounts, CORS, rate limiting, security headers, /healthz, telemetry, API explorer.
services.AddServiceDefaults(builder.Configuration);
services.AddFrontPack(builder.Configuration);
services.AddSlideshow();

var app = builder.Build();

await app.Services.InitializeUserStoreAsync();
await app.Services.InitializeSlideshowStorageAsync();

app.UseServiceDefaults("FrontPack API");

var api = app.MapGroup("/api/v1").RequireAuthorization(Auth.ApiPolicy);
app.MapFrontPack(api);
app.MapSlideshow(api);

app.Run();

namespace FrontPack.Hosts
{
    /// <summary>Names this host to WebApplicationFactory in the tests.</summary>
    public sealed class CombinedHost;
}
