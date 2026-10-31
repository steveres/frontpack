using System.Security.Claims;
using Microsoft.Extensions.Options;
using Slideshow.Api.Data;
using Slideshow.Api.Identity;

namespace Slideshow.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccount(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/account").WithTags("Account");

        g.MapPost("/", Register).WithSummary("Register: create the caller's API account (idempotent).");
        g.MapGet("/", Get).WithSummary("The caller's account, usage and quota.");
        g.MapPost("/logout", Logout).WithSummary("Sign out everywhere: rejects all tokens issued until now.");
        g.MapDelete("/", Delete).WithSummary("Delete the account, all albums and files, and the Entra user.");
    }

    private static async Task<IResult> Register(HttpContext http, IUserStore store, IEnumerable<IServiceAccountData> services, IOptions<LimitsOptions> limits, ILogger<Program> log, CancellationToken ct)
    {
        if (http.Account() is { } existing) return Results.Ok(await ToDto(services, existing, limits.Value, ct));

        var user = new User
        {
            Id = http.User.UserId(),
            DisplayName = Truncate(http.User.FindFirstValue("name"), 256),
            Email = Truncate(http.User.FindFirstValue("email") ?? http.User.FindFirstValue("preferred_username"), 320),
            CreatedAt = DateTime.UtcNow,
        };
        if (!await store.TryAddUserAsync(user, ct))
        {
            // Two registrations raced; the other one won.
            var winner = await store.GetUserAsync(user.Id, ct) ?? throw new InvalidOperationException("Registration conflict, but no user.");
            return Results.Ok(await ToDto(services, winner, limits.Value, ct));
        }
        log.LogInformation("Registered user {UserId}", user.Id);
        return Results.Created("/api/v1/account", await ToDto(services, user, limits.Value, ct));
    }

    private static async Task<IResult> Get(HttpContext http, IEnumerable<IServiceAccountData> services, IOptions<LimitsOptions> limits, CancellationToken ct) =>
        http.Account() is { } user ? Results.Ok(await ToDto(services, user, limits.Value, ct)) : Problems.NotRegistered();

    private static async Task<IResult> Logout(HttpContext http, IUserStore store, IIdentityDirectory directory, ILogger<Program> log, CancellationToken ct)
    {
        var userId = http.User.UserId();
        await store.SetTokensValidAfterAsync(userId, DateTime.UtcNow, ct);
        try { await directory.RevokeSessionsAsync(userId, ct); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Our cut-off already rejects the old access tokens; Entra's refresh tokens will lapse on their own.
            log.LogWarning(e, "Could not revoke Entra sessions for {UserId}", userId);
        }
        return Results.NoContent();
    }

    /// <summary>Data first, identity last: if any step fails the user can still sign in and retry.</summary>
    private static async Task<IResult> Delete(HttpContext http, IUserStore store, IEnumerable<IServiceAccountData> services, IIdentityDirectory directory, ILogger<Program> log, CancellationToken ct)
    {
        var userId = http.User.UserId();
        // Each service's data, then the user: a retry after a failure picks up where it stopped.
        foreach (var service in services) await service.DeleteUserDataAsync(userId, ct);
        await store.DeleteUserAsync(userId, ct);

        try { await directory.DeleteUserAsync(userId, ct); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Data for {UserId} was deleted but the Entra user could not be", userId);
            return Problems.Of(502, "identity_delete_failed", "Your data was deleted, but your sign-in could not be removed. Try again.");
        }
        log.LogInformation("Deleted account {UserId}", userId);
        return Results.NoContent();
    }

    /// <summary>Usage is totalled across the services; the quota covers all of them.</summary>
    private static async Task<AccountDto> ToDto(IEnumerable<IServiceAccountData> services, User u, LimitsOptions limits, CancellationToken ct)
    {
        var usage = await Task.WhenAll(services.Select(s => s.GetUsageAsync(u.Id, ct)));
        long Count(string kind) => usage.Sum(x => x.Counts.GetValueOrDefault(kind));
        return new AccountDto(u.Id, u.DisplayName, u.Email, u.CreatedAt,
            new UsageDto((int)Count("albums"), (int)Count("files"), usage.Sum(x => x.Bytes), limits.QuotaBytesPerUser));
    }

    private static string? Truncate(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
