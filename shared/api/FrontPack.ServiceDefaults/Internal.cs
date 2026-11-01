using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FrontPack.ServiceDefaults;

/// <summary>
/// "Internal" section: the port that only the other FrontPack APIs reach (in Azure, a container port with no
/// public ingress). Unset: no internal endpoints.
/// </summary>
public sealed class InternalOptions
{
    public int? Port { get; set; }
}

/// <summary>
/// Calls between the FrontPack APIs: /internal/..., answered only for requests that arrived on the internal
/// port. The check is on the connection's port, not the Host header, which a caller could set to anything.
/// Nothing else protects these endpoints, so they must never be reachable from outside.
/// </summary>
public static class InternalEndpoints
{
    public static RouteGroupBuilder MapInternal(this WebApplication app)
    {
        var port = app.Services.GetRequiredService<IOptions<InternalOptions>>().Value.Port
            ?? throw new InvalidOperationException("Internal:Port is not set.");
        return app.MapGroup("/internal").AllowAnonymous().DisableRateLimiting().ExcludeFromDescription()
            .AddEndpointFilter((ctx, next) =>
                ctx.HttpContext.Connection.LocalPort == port ? next(ctx) : ValueTask.FromResult<object?>(Results.NotFound()));
    }

    /// <summary>
    /// The service's side of the FrontPack account, for FrontPack's API: GET /internal/users/{id}/usage and
    /// DELETE /internal/users/{id}, served by the service's IServiceAccountData.
    /// </summary>
    public static WebApplication MapInternalAccountData(this WebApplication app)
    {
        var users = app.MapInternal().MapGroup("/users/{userId}");
        users.MapGet("/usage", (string userId, IServiceAccountData data, CancellationToken ct) => data.GetUsageAsync(userId, ct));
        users.MapDelete("/", async (string userId, IServiceAccountData data, CancellationToken ct) =>
        {
            await data.DeleteUserDataAsync(userId, ct);
            return Results.NoContent();
        });
        return app;
    }
}
