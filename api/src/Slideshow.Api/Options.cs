namespace Slideshow.Api;

/// <summary>Token validation for Entra External ID ("Auth" section).</summary>
public sealed class AuthOptions
{
    /// <summary>e.g. https://contoso.ciamlogin.com/{tenantId}/v2.0</summary>
    public string Authority { get; set; } = "";
    /// <summary>The API app registration's client id (and/or its api:// URI).</summary>
    public string[] Audiences { get; set; } = [];
    /// <summary>Delegated scope the token must carry in "scp".</summary>
    public string RequiredScope { get; set; } = "Slideshow.Access";
    /// <summary>
    /// Entra dates its tokens' "iat" (issued at) about 5 minutes before they're really issued, to allow
    /// for clock differences. The logout cut-off allows for it, or a sign-in in the first minutes after
    /// a logout would get a token that looks older than the logout, and be turned away.
    /// </summary>
    public TimeSpan IssuedAtBackdating { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>"Tables" section: where users, albums and media details are kept.</summary>
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

/// <summary>"Graph" section: Microsoft Graph access to the customer tenant. Leave TenantId empty to disable.</summary>
public sealed class GraphOptions
{
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    /// <summary>Authenticate as ClientId using the managed identity as a federated credential (preferred in Azure).</summary>
    public bool UseManagedIdentityFederation { get; set; } = true;
    /// <summary>Client id of a user-assigned managed identity; empty for system-assigned.</summary>
    public string? ManagedIdentityClientId { get; set; }
    /// <summary>Fallback for local testing against a real tenant; never commit it.</summary>
    public string? ClientSecret { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId);
}

/// <summary>"Limits" section.</summary>
public sealed class LimitsOptions
{
    public long MaxImageBytes { get; set; } = 50L * 1024 * 1024;
    public long MaxVideoBytes { get; set; } = 1024L * 1024 * 1024;
    public long MaxMusicBytes { get; set; } = 50L * 1024 * 1024;
    public long QuotaBytesPerUser { get; set; } = 10L * 1024 * 1024 * 1024;
    public int MaxFilesPerAlbum { get; set; } = 5000;
    public int MaxAlbumsPerUser { get; set; } = 500;
    /// <summary>Requests per user per minute.</summary>
    public int RequestsPerMinute { get; set; } = 600;

    public long MaxUploadBytes => Math.Max(MaxImageBytes, Math.Max(MaxVideoBytes, MaxMusicBytes));
}

/// <summary>"Cors" section.</summary>
public sealed class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = [];
}
