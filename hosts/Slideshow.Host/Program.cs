// The Slideshow service's API: albums, media, sharing (the slideshow-api container app in the FrontPack
// environment). FrontPack's API reaches its side of the account on the internal port.
using Azure.Core;
using Azure.Identity;
using Slideshow.Api;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

// Signing in to Table, Blob and Queue Storage in Azure: the API's managed identity, straight away (no credential
// chain), and one credential for all of them so they share one storage token. AZURE_CLIENT_ID picks the identity.
services.AddSingleton<TokenCredential>(_ => builder.Configuration["AZURE_CLIENT_ID"] is { Length: > 0 } clientId
    ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
    : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));

services.AddServiceDefaults(builder.Configuration);
services.AddSlideshow();

var app = builder.Build();

await app.Services.InitializeSlideshowStorageAsync();

app.UseServiceDefaults("Slideshow API");

var api = app.MapGroup("/api/v1").RequireAuthorization(Auth.ApiPolicy);
app.MapSlideshow(api);
app.MapInternalAccountData(); // usage and account deletion, for FrontPack's API

app.Run();

namespace FrontPack.Hosts
{
    /// <summary>Names this host to WebApplicationFactory in the tests.</summary>
    public sealed class SlideshowHost;
}
