using Microsoft.Extensions.Options;
using Slideshow.Api.Data;
using Slideshow.Api.Endpoints;
using Slideshow.Api.Media;
using Slideshow.Api.Storage;

namespace Slideshow.Api;

/// <summary>
/// The Slideshow service: albums, their photos and videos, their music (from the music library), share links and public albums. A host
/// adds it with AddSlideshow and MapSlideshow, after FrontPack's service defaults (sign-in etc.).
/// </summary>
public static class SlideshowApi
{
    public static IServiceCollection AddSlideshow(this IServiceCollection services)
    {
        services.AddOptions<TablesOptions>().BindConfiguration("Tables");
        services.AddOptions<StorageOptions>().BindConfiguration("Storage");
        services.AddOptions<LimitsOptions>().BindConfiguration("Limits");
        services.AddOptions<ResizeOptions>().BindConfiguration("Resize");

        // Albums and media details: Azure Table Storage in the cloud, memory (saved to a file) locally and in tests.
        services.AddSingleton<ISlideshowStore>(sp =>
            sp.GetRequiredService<IOptions<TablesOptions>>().Value.Provider.Equals("AzureTable", StringComparison.OrdinalIgnoreCase)
                ? ActivatorUtilities.CreateInstance<TableDataStore>(sp)
                : ActivatorUtilities.CreateInstance<LocalDataStore>(sp));

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

        // The music library trips choose from; admins are the users given the API's Music.Admin app role in Entra.
        services.AddSingleton<MusicLibrary>();
        services.AddAuthorizationBuilder().AddPolicy(MusicEndpoints.AdminPolicy, p => p.RequireClaim("roles", MusicEndpoints.AdminRole));
        services.AddHostedService<LibraryMigration>(); // one-off: deletes uploaded music, times older videos

        // Slideshow's part of an account: usage on the account page, and what deleting the account deletes.
        services.AddScoped<IServiceAccountData, SlideshowAccountData>();
        return services;
    }

    /// <summary>
    /// The routes: /api/v1/albums and /api/v1/admin/music (signed in), /api/v1/shared, /api/v1/public-albums
    /// and /api/v1/music (anyone).
    /// </summary>
    public static WebApplication MapSlideshow(this WebApplication app, RouteGroupBuilder api)
    {
        var limits = app.Services.GetRequiredService<IOptions<LimitsOptions>>().Value;
        api.MapAlbums(limits.MaxUploadBytes);
        api.MapMusicAdmin(limits.MaxMusicBytes);
        app.MapGroup("/api/v1/shared").AllowAnonymous().MapShared(); // share links: no sign-in
        app.MapGroup("/api/v1/public-albums").AllowAnonymous().MapPublicAlbums(); // the albums anyone can watch
        app.MapGroup("/api/v1/music").AllowAnonymous().MapMusic(); // the library, played by shared slideshows too
        return app;
    }

    /// <summary>
    /// In Azure the tables, the media container and the resize queue come from the Bicep, so starting up makes
    /// no storage calls. The local emulator (Azurite, reached by connection string) starts empty: create them there.
    /// </summary>
    public static async Task InitializeSlideshowStorageAsync(this IServiceProvider services)
    {
        if (services.GetRequiredService<ISlideshowStore>() is TableDataStore tables
            && !string.IsNullOrEmpty(services.GetRequiredService<IOptions<TablesOptions>>().Value.ConnectionString))
            await tables.EnsureTablesAsync(default);
        if (services.GetRequiredService<IBlobStore>() is AzureBlobStore azure
            && !string.IsNullOrEmpty(services.GetRequiredService<IOptions<StorageOptions>>().Value.ConnectionString))
            await azure.EnsureContainerAsync(default);
        if (services.GetRequiredService<IResizeQueue>() is AzureResizeQueue queue
            && !string.IsNullOrEmpty(services.GetRequiredService<IOptions<ResizeOptions>>().Value.ConnectionString))
            await queue.EnsureQueueAsync(default);
    }
}

/// <summary>The log category for Slideshow's endpoints.</summary>
public sealed class SlideshowLog;
