namespace Slideshow.Api.Data;

/// <summary>
/// FrontPack accounts: one row per signed-up user, shared by every service. Azure Table Storage in the
/// cloud, memory (optionally saved to a file) locally and in tests. Reads return copies.
/// </summary>
public interface IUserStore
{
    Task<User?> GetUserAsync(string userId, CancellationToken ct);
    /// <summary>Adds the user; false if one with that id already exists.</summary>
    Task<bool> TryAddUserAsync(User user, CancellationToken ct);
    /// <summary>Logout: tokens issued before `cutoff` are rejected. Nothing happens for an unknown user.</summary>
    Task SetTokensValidAfterAsync(string userId, DateTime cutoff, CancellationToken ct);
    Task DeleteUserAsync(string userId, CancellationToken ct);
}
