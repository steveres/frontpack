namespace FrontPack.ServiceDefaults;

/// <summary>A FrontPack account: one per signed-up user, shared by every service.</summary>
public sealed class User
{
    /// <summary>The Entra object id ("oid" claim).</summary>
    public required string Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Tokens issued before this (UTC) are rejected: set by logout.</summary>
    public DateTime? TokensValidAfter { get; set; }

    public User Clone() => (User)MemberwiseClone();
}

/// <summary>
/// FrontPack accounts: one row per signed-up user, read by every service's sign-in check. Azure Table
/// Storage in the cloud, memory (optionally saved to a file) locally and in tests. Reads return copies.
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
