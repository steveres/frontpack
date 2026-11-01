using System.Net.Http.Json;

namespace FrontPack.Api;

/// <summary>One entry of the "Services" section: a service whose data belongs to the account, and where to reach it.</summary>
public sealed class ServiceEndpoint
{
    /// <summary>e.g. "slideshow".</summary>
    public string Name { get; set; } = "";
    /// <summary>The service's internal port, e.g. http://slideshow-api:8081 (reachable only inside the Container Apps environment).</summary>
    public string InternalUrl { get; set; } = "";
}

/// <summary>A service running as its own API: its usage and account deletion over its internal endpoints.</summary>
public sealed class RemoteServiceAccountData(string service, HttpClient http) : IServiceAccountData
{
    public string Service => service;

    public static string ClientName(string service) => "service:" + service;

    public async Task<ServiceUsage> GetUsageAsync(string userId, CancellationToken ct) =>
        await http.GetFromJsonAsync<ServiceUsage>($"internal/users/{Uri.EscapeDataString(userId)}/usage", ct)
            ?? throw new InvalidOperationException($"{service} returned no usage.");

    public async Task DeleteUserDataAsync(string userId, CancellationToken ct) =>
        (await http.DeleteAsync($"internal/users/{Uri.EscapeDataString(userId)}", ct)).EnsureSuccessStatusCode();
}
