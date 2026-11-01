// FrontPack's own API: accounts, development sign-in, Microsoft Graph (the frontpack-api container app). The
// account's usage and deletion reach each service through its internal port ("Services" settings).
using Azure.Core;
using Azure.Identity;
using FrontPack.Api;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

// Signing in to Table Storage in Azure: the API's managed identity, straight away (no credential chain).
// AZURE_CLIENT_ID picks the identity.
services.AddSingleton<TokenCredential>(_ => builder.Configuration["AZURE_CLIENT_ID"] is { Length: > 0 } clientId
    ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
    : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));

services.AddServiceDefaults(builder.Configuration);
services.AddFrontPack(builder.Configuration);

var app = builder.Build();

await app.Services.InitializeUserStoreAsync();

app.UseServiceDefaults("FrontPack API");

var api = app.MapGroup("/api/v1").RequireAuthorization(Auth.ApiPolicy);
app.MapFrontPack(api);

app.Run();

namespace FrontPack.Hosts
{
    /// <summary>Names this host to WebApplicationFactory in the tests.</summary>
    public sealed class FrontPackHost;
}
