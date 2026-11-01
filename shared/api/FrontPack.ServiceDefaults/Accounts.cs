using Microsoft.AspNetCore.Http;

namespace FrontPack.ServiceDefaults;

/// <summary>What a user keeps in one service: counts by kind (e.g. "albums", "files") and bytes stored.</summary>
public sealed record ServiceUsage(IReadOnlyDictionary<string, long> Counts, long Bytes);

/// <summary>
/// A service's side of the FrontPack account: the account page totals its usage, and deleting the account
/// deletes its data. Each service implements it for its own data.
/// </summary>
public interface IServiceAccountData
{
    /// <summary>The service's name, e.g. "slideshow".</summary>
    string Service { get; }
    Task<ServiceUsage> GetUsageAsync(string userId, CancellationToken ct);
    /// <summary>Deletes everything the user keeps in the service. Safe to repeat after a failure.</summary>
    Task DeleteUserDataAsync(string userId, CancellationToken ct);
}

/// <summary>RFC 7807 errors with a stable machine-readable "code".</summary>
public static class Problems
{
    public static IResult Of(int status, string code, string detail) =>
        Results.Problem(statusCode: status, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult NotRegistered() => Of(403, "account_not_registered", "Register with POST /api/v1/account first.");
    public static IResult Validation(string detail) => Of(400, "validation_failed", detail);

    /// <summary>Endpoint filter: only callers with a FrontPack account get through.</summary>
    public static async ValueTask<object?> RequireRegistered(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next) =>
        ctx.HttpContext.Account() is null ? NotRegistered() : await next(ctx);
}
