using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FrontPack.ServiceDefaults;

/// <summary>"Users" section: where the FrontPack accounts are kept. FrontPack writes them; every service reads them.</summary>
public sealed class UsersOptions
{
    /// <summary>"AzureTable" (Azure Table Storage or Azurite) or "Local" (memory, optionally saved to LocalPath).</summary>
    public string Provider { get; set; } = "Local";
    /// <summary>Local: a JSON file the accounts are saved to; empty keeps them in memory only (tests).</summary>
    public string? LocalPath { get; set; } = "App_Data/users.json";
    /// <summary>AzureTable: e.g. https://account.table.core.windows.net (managed identity), or a connection string for Azurite.</summary>
    public string? ServiceUri { get; set; }
    public string? ConnectionString { get; set; }
}

public static class UserStores
{
    /// <summary>Registers IUserStore from the "Users" section. Table Storage signs in with the app's TokenCredential.</summary>
    public static IServiceCollection AddUserStore(this IServiceCollection services)
    {
        services.AddOptions<UsersOptions>().BindConfiguration("Users");
        services.AddSingleton<IUserStore>(sp =>
            sp.GetRequiredService<IOptions<UsersOptions>>().Value.Provider.Equals("AzureTable", StringComparison.OrdinalIgnoreCase)
                ? ActivatorUtilities.CreateInstance<TableUserStore>(sp)
                : ActivatorUtilities.CreateInstance<LocalUserStore>(sp));
        return services;
    }

    /// <summary>The local emulator (Azurite, reached by connection string) starts empty: create the table there.
    /// In Azure the Bicep creates it, so starting up makes no storage calls.</summary>
    public static async Task InitializeUserStoreAsync(this IServiceProvider services)
    {
        if (services.GetRequiredService<IUserStore>() is TableUserStore tables
            && !string.IsNullOrEmpty(services.GetRequiredService<IOptions<UsersOptions>>().Value.ConnectionString))
            await tables.EnsureTableAsync(default);
    }
}

/// <summary>The Users table: one row per account, partition = user id, row = "user".</summary>
public sealed class TableUserStore : IUserStore
{
    private const string UserRow = "user";
    private readonly TableClient _users;

    /// <param name="credential">Signs in to Azure (not used with a connection string, i.e. Azurite).</param>
    public TableUserStore(IOptions<UsersOptions> options, TokenCredential credential)
    {
        var o = options.Value;
        var service = !string.IsNullOrEmpty(o.ConnectionString)
            ? new TableServiceClient(o.ConnectionString)
            : new TableServiceClient(new Uri(o.ServiceUri ?? throw new InvalidOperationException("Users:ServiceUri is not set.")), credential);
        _users = service.GetTableClient("Users");
    }

    public async Task EnsureTableAsync(CancellationToken ct) => await _users.CreateIfNotExistsAsync(ct);

    private static DateTimeOffset? Utc(DateTime? v) => v is { } d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)) : null;
    private static DateTime? Utc(TableEntity e, string name) => e.GetDateTimeOffset(name)?.UtcDateTime;

    private static TableEntity ToEntity(User u) => new(u.Id, UserRow)
    {
        ["DisplayName"] = u.DisplayName,
        ["Email"] = u.Email,
        ["CreatedAt"] = Utc(u.CreatedAt),
        ["TokensValidAfter"] = Utc(u.TokensValidAfter),
    };

    private static User ToUser(TableEntity e) => new()
    {
        Id = e.PartitionKey,
        DisplayName = e.GetString("DisplayName"),
        Email = e.GetString("Email"),
        CreatedAt = Utc(e, "CreatedAt") ?? default,
        TokensValidAfter = Utc(e, "TokensValidAfter"),
    };

    public async Task<User?> GetUserAsync(string userId, CancellationToken ct)
    {
        var r = await _users.GetEntityIfExistsAsync<TableEntity>(userId, UserRow, cancellationToken: ct);
        return r.HasValue ? ToUser(r.Value!) : null;
    }

    public async Task<bool> TryAddUserAsync(User user, CancellationToken ct)
    {
        try { await _users.AddEntityAsync(ToEntity(user), ct); return true; }
        catch (RequestFailedException e) when (e.Status == 409) { return false; }
    }

    public async Task SetTokensValidAfterAsync(string userId, DateTime cutoff, CancellationToken ct)
    {
        try { await _users.UpdateEntityAsync(new TableEntity(userId, UserRow) { ["TokensValidAfter"] = Utc(cutoff) }, ETag.All, TableUpdateMode.Merge, ct); }
        catch (RequestFailedException e) when (e.Status == 404) { }
    }

    public async Task DeleteUserAsync(string userId, CancellationToken ct)
    {
        try { await _users.DeleteEntityAsync(userId, UserRow, ETag.All, ct); }
        catch (RequestFailedException e) when (e.Status == 404) { }
    }
}

/// <summary>
/// Local development and tests: the accounts live in memory and, when Users:LocalPath is set, in that JSON
/// file. The file is read again when it changes, so APIs running side by side locally see each other's
/// registrations and sign-outs.
/// </summary>
public sealed class LocalUserStore : IUserStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _gate = new();
    private readonly string? _path;
    private Dictionary<string, User> _users = [];
    private DateTime _loadedWrite;

    public LocalUserStore(IOptions<UsersOptions> options)
    {
        var path = options.Value.LocalPath;
        _path = string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);
    }

    /// <summary>Picks up changes another process saved. Call inside the lock.</summary>
    private void Refresh()
    {
        if (_path is null || !File.Exists(_path)) return;
        var written = File.GetLastWriteTimeUtc(_path);
        if (written == _loadedWrite) return;
        _users = JsonSerializer.Deserialize<Dictionary<string, User>>(File.ReadAllText(_path), Json) ?? [];
        _loadedWrite = written;
    }

    private T Read<T>(Func<Dictionary<string, User>, T> read) { lock (_gate) { Refresh(); return read(_users); } }

    private T Write<T>(Func<Dictionary<string, User>, T> write)
    {
        lock (_gate)
        {
            Refresh();
            var result = write(_users);
            if (_path is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + "." + Environment.ProcessId + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_users, Json));
                File.Move(temp, _path, overwrite: true);
                _loadedWrite = File.GetLastWriteTimeUtc(_path);
            }
            return result;
        }
    }

    public Task<User?> GetUserAsync(string userId, CancellationToken ct) =>
        Task.FromResult(Read(u => u.GetValueOrDefault(userId)?.Clone()));

    public Task<bool> TryAddUserAsync(User user, CancellationToken ct) =>
        Task.FromResult(Write(u => u.TryAdd(user.Id, user.Clone())));

    public Task SetTokensValidAfterAsync(string userId, DateTime cutoff, CancellationToken ct) =>
        Task.FromResult(Write(u => { if (u.TryGetValue(userId, out var x)) x.TokensValidAfter = cutoff; return 0; }));

    public Task DeleteUserAsync(string userId, CancellationToken ct) =>
        Task.FromResult(Write(u => u.Remove(userId)));
}
