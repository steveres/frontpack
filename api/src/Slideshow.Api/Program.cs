using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using Slideshow.Api;
using Slideshow.Api.Data;
using Slideshow.Api.Endpoints;
using Slideshow.Api.Identity;
using Slideshow.Api.Media;
using Slideshow.Api.Storage;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

// Sign-in, CORS, rate limiting, security headers, /healthz and telemetry: the same in every FrontPack service.
services.AddServiceDefaults(builder.Configuration);

services.AddOptions<TablesOptions>().BindConfiguration("Tables");
services.AddOptions<StorageOptions>().BindConfiguration("Storage");
services.AddOptions<GraphOptions>().BindConfiguration("Graph");
services.AddOptions<LimitsOptions>().BindConfiguration("Limits");
services.AddOptions<ResizeOptions>().BindConfiguration("Resize");

// Signing in to Table and Blob Storage in Azure: the API's managed identity, straight away (no credential
// chain), and one credential for both so they share one storage token. AZURE_CLIENT_ID picks the identity.
services.AddSingleton<TokenCredential>(_ => builder.Configuration["AZURE_CLIENT_ID"] is { Length: > 0 } clientId
    ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId))
    : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));

// Users, albums and media details: Azure Table Storage in the cloud, memory (saved to a file) locally and in tests.
// One store holds both for now; FrontPack (users) and Slideshow (albums, media, shares) each see only their part.
services.AddSingleton<IUserStore>(sp =>
    sp.GetRequiredService<IOptions<TablesOptions>>().Value.Provider.Equals("AzureTable", StringComparison.OrdinalIgnoreCase)
        ? ActivatorUtilities.CreateInstance<TableDataStore>(sp)
        : ActivatorUtilities.CreateInstance<LocalDataStore>(sp));
services.AddSingleton(sp => (ISlideshowStore)sp.GetRequiredService<IUserStore>());

// Each service's part of an account: usage on the account page, and what deleting the account deletes.
services.AddScoped<IServiceAccountData, SlideshowAccountData>();

// Media bytes: Azure Blob Storage in the cloud, a folder locally.
services.AddSingleton<IBlobStore>(sp =>
    sp.GetRequiredService<IOptions<StorageOptions>>().Value.Provider.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase)
        ? ActivatorUtilities.CreateInstance<AzureBlobStore>(sp)
        : ActivatorUtilities.CreateInstance<FileSystemBlobStore>(sp));

// Photos waiting for their smaller versions: an Azure Storage queue in the cloud, memory locally.
// The worker makes them in the background, one photo at a time.
services.AddSingleton<IResizeQueue>(sp =>
    sp.GetRequiredService<IOptions<ResizeOptions>>().Value.Provider.Equals("AzureQueue", StringComparison.OrdinalIgnoreCase)
        ? ActivatorUtilities.CreateInstance<AzureResizeQueue>(sp)
        : ActivatorUtilities.CreateInstance<MemoryResizeQueue>(sp));
services.AddHostedService<ResizeWorker>();

// Entra user operations via Microsoft Graph, when configured.
services.AddHttpClient<GraphIdentityDirectory>();
services.AddSingleton<NoOpIdentityDirectory>();
services.AddScoped<IIdentityDirectory>(sp => sp.GetRequiredService<IOptions<GraphOptions>>().Value.Enabled
    ? sp.GetRequiredService<GraphIdentityDirectory>()
    : sp.GetRequiredService<NoOpIdentityDirectory>());

services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    // Lets the API explorer send "Authorization: Bearer <token>".
    var scheme = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
    doc.Components ??= new OpenApiComponents();
    doc.Components.SecuritySchemes["Bearer"] = scheme;
    doc.SecurityRequirements.Add(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [],
    });
    return Task.CompletedTask;
}));

var app = builder.Build();

await InitializeStorageAsync(app.Services);

app.UseServiceDefaults();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                                                      // /openapi/v1.json
    app.MapScalarApiReference(o => o.WithTitle("Slideshow API"));         // /scalar: browse and try the API
    app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
}
app.MapDevTokens(); // Development only: website sign-in without an Entra tenant

var api = app.MapGroup("/api/v1").RequireAuthorization(Auth.ApiPolicy);
api.MapAccount();
api.MapAlbums(app.Services.GetRequiredService<IOptions<LimitsOptions>>().Value.MaxUploadBytes);
app.MapGroup("/api/v1/shared").AllowAnonymous().MapShared(); // share links: no sign-in
app.MapGroup("/api/v1/public-albums").AllowAnonymous().MapPublicAlbums(); // the albums anyone can watch

app.Run();

// In Azure the tables, the media container and the resize queue come from main.bicep, so starting up makes
// no storage calls. The local emulator (Azurite, reached by connection string) starts empty: create them there.
static async Task InitializeStorageAsync(IServiceProvider services)
{
    if (services.GetRequiredService<IUserStore>() is TableDataStore tables
        && !string.IsNullOrEmpty(services.GetRequiredService<IOptions<TablesOptions>>().Value.ConnectionString))
        await tables.EnsureTablesAsync(default);
    if (services.GetRequiredService<IBlobStore>() is AzureBlobStore azure
        && !string.IsNullOrEmpty(services.GetRequiredService<IOptions<StorageOptions>>().Value.ConnectionString))
        await azure.EnsureContainerAsync(default);
    if (services.GetRequiredService<IResizeQueue>() is AzureResizeQueue queue
        && !string.IsNullOrEmpty(services.GetRequiredService<IOptions<ResizeOptions>>().Value.ConnectionString))
        await queue.EnsureQueueAsync(default);
}

public partial class Program; // for WebApplicationFactory in tests
