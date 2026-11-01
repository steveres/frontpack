using Microsoft.Extensions.Options;

namespace FrontPack.Api;

/// <summary>
/// FrontPack's own API: the account (register, usage, sign out everywhere, delete), development sign-in and
/// the Entra side of the account (Microsoft Graph). A host adds it with AddFrontPack and MapFrontPack, after
/// the service defaults; each service's IServiceAccountData feeds the account's usage and deletion.
/// </summary>
public static class FrontPackApi
{
    public static IServiceCollection AddFrontPack(this IServiceCollection services)
    {
        services.AddOptions<GraphOptions>().BindConfiguration("Graph");
        services.AddOptions<AccountOptions>().BindConfiguration("Limits");

        // Entra user operations via Microsoft Graph, when configured.
        services.AddHttpClient<GraphIdentityDirectory>();
        services.AddSingleton<NoOpIdentityDirectory>();
        services.AddScoped<IIdentityDirectory>(sp => sp.GetRequiredService<IOptions<GraphOptions>>().Value.Enabled
            ? sp.GetRequiredService<GraphIdentityDirectory>()
            : sp.GetRequiredService<NoOpIdentityDirectory>());
        return services;
    }

    /// <summary>The routes: /api/v1/account (signed in) and, in Development only, /dev/token.</summary>
    public static WebApplication MapFrontPack(this WebApplication app, RouteGroupBuilder api)
    {
        app.MapDevTokens(); // Development only: website sign-in without an Entra tenant
        api.MapAccount();
        return app;
    }
}

/// <summary>The log category for FrontPack's endpoints.</summary>
public sealed class FrontPackLog;

/// <summary>"Limits" section: the storage quota shown on the account page (each service enforces it on its own data).</summary>
public sealed class AccountOptions
{
    public long QuotaBytesPerUser { get; set; } = 10L * 1024 * 1024 * 1024;
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

public sealed record UsageDto(int Albums, int Files, long Bytes, long QuotaBytes);
public sealed record AccountDto(string Id, string? DisplayName, string? Email, DateTime CreatedAt, UsageDto Usage);
