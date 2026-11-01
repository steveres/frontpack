namespace Slideshow.Api;

/// <summary>"Tables" section: where albums, media details and share links are kept.</summary>
public sealed class TablesOptions
{
    /// <summary>"AzureTable" (Azure Table Storage or Azurite) or "Local" (memory, optionally saved to LocalPath).</summary>
    public string Provider { get; set; } = "Local";
    /// <summary>Local: a JSON file the data is saved to; empty keeps it in memory only (tests).</summary>
    public string? LocalPath { get; set; } = "App_Data/data.json";
    /// <summary>AzureTable: e.g. https://account.table.core.windows.net (managed identity), or a connection string for Azurite.</summary>
    public string? ServiceUri { get; set; }
    public string? ConnectionString { get; set; }
}

/// <summary>"Storage" section.</summary>
public sealed class StorageOptions
{
    /// <summary>"AzureBlob" or "FileSystem".</summary>
    public string Provider { get; set; } = "FileSystem";
    /// <summary>FileSystem: root folder.</summary>
    public string RootPath { get; set; } = "App_Data/media";
    /// <summary>AzureBlob: e.g. https://account.blob.core.windows.net (managed identity), or a connection string for Azurite.</summary>
    public string? BlobServiceUri { get; set; }
    public string? ConnectionString { get; set; }
    public string ContainerName { get; set; } = "media";
}

/// <summary>"Resize" section: the queue of photos waiting for their smaller versions.</summary>
public sealed class ResizeOptions
{
    /// <summary>"AzureQueue" (Azure Storage Queues or Azurite) or "Memory" (lost when the API stops).</summary>
    public string Provider { get; set; } = "Memory";
    /// <summary>AzureQueue: e.g. https://account.queue.core.windows.net (managed identity), or a connection string for Azurite.</summary>
    public string? QueueServiceUri { get; set; }
    public string? ConnectionString { get; set; }
    public string QueueName { get; set; } = "resize";
    /// <summary>Tries per photo before giving up on it (it then stays full size only).</summary>
    public int MaxAttempts { get; set; } = 5;
}


/// <summary>"Limits" section (RequestsPerMinute, also here, is shared by every service: see RateLimitOptions).</summary>
public sealed class LimitsOptions
{
    public long MaxImageBytes { get; set; } = 50L * 1024 * 1024;
    public long MaxVideoBytes { get; set; } = 1024L * 1024 * 1024;
    public long MaxMusicBytes { get; set; } = 50L * 1024 * 1024;
    public long QuotaBytesPerUser { get; set; } = 10L * 1024 * 1024 * 1024;
    public int MaxFilesPerAlbum { get; set; } = 5000;
    public int MaxAlbumsPerUser { get; set; } = 500;

    public long MaxUploadBytes => Math.Max(MaxImageBytes, Math.Max(MaxVideoBytes, MaxMusicBytes));
}
