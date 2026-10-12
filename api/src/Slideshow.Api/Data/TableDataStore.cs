using System.Globalization;
using Azure;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Slideshow.Api.Data;

/// <summary>
/// Azure Table Storage (or Azurite). Four tables, each partitioned so a query reads one partition:
/// Users (user id / "user"), Albums (owner / album), Media (owner / "album_media"), Shares (code / "share").
/// </summary>
public sealed class TableDataStore : IDataStore
{
    private const string UserRow = "user", ShareRow = "share";
    private readonly TableClient _users, _albums, _media, _shares;

    public TableDataStore(IOptions<TablesOptions> options)
    {
        var o = options.Value;
        var service = !string.IsNullOrEmpty(o.ConnectionString)
            ? new TableServiceClient(o.ConnectionString)
            : new TableServiceClient(new Uri(o.ServiceUri ?? throw new InvalidOperationException("Tables:ServiceUri is not set.")),
                new DefaultAzureCredential());
        _users = service.GetTableClient("Users");
        _albums = service.GetTableClient("Albums");
        _media = service.GetTableClient("Media");
        _shares = service.GetTableClient("Shares");
    }

    /// <summary>Creates the tables if missing.</summary>
    public async Task EnsureTablesAsync(CancellationToken ct)
    {
        foreach (var table in new[] { _users, _albums, _media, _shares }) await table.CreateIfNotExistsAsync(ct);
    }

    private static string Key(Guid id) => id.ToString("N");
    private static string MediaRow(Guid albumId, Guid mediaId) => $"{Key(albumId)}_{Key(mediaId)}";
    private static DateTimeOffset? Utc(DateTime? v) => v is { } d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)) : null;
    private static DateTime? Utc(TableEntity e, string name) => e.GetDateTimeOffset(name)?.UtcDateTime;
    private static bool Is(RequestFailedException e, int status) => e.Status == status;

    private async Task<TableEntity?> GetAsync(TableClient table, string pk, string rk, CancellationToken ct)
    {
        var r = await table.GetEntityIfExistsAsync<TableEntity>(pk, rk, cancellationToken: ct);
        return r.HasValue ? r.Value : null;
    }

    private static async Task<List<TableEntity>> QueryAsync(TableClient table, string filter, CancellationToken ct, IEnumerable<string>? select = null)
    {
        var list = new List<TableEntity>();
        await foreach (var e in table.QueryAsync<TableEntity>(filter, select: select, cancellationToken: ct)) list.Add(e);
        return list;
    }

    private static async Task<bool> DeleteAsync(TableClient table, string pk, string rk, CancellationToken ct)
    {
        try { await table.DeleteEntityAsync(pk, rk, ETag.All, ct); return true; }
        catch (RequestFailedException e) when (Is(e, 404)) { return false; }
    }

    private static async Task<bool> TryAddAsync(TableClient table, TableEntity entity, CancellationToken ct)
    {
        try { await table.AddEntityAsync(entity, ct); return true; }
        catch (RequestFailedException e) when (Is(e, 409)) { return false; }
    }

    private static async Task MergeIfExistsAsync(TableClient table, TableEntity changes, CancellationToken ct)
    {
        try { await table.UpdateEntityAsync(changes, ETag.All, TableUpdateMode.Merge, ct); }
        catch (RequestFailedException e) when (Is(e, 404)) { }
    }

    // ───────── Users ─────────

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

    public async Task<User?> GetUserAsync(string userId, CancellationToken ct) =>
        await GetAsync(_users, userId, UserRow, ct) is { } e ? ToUser(e) : null;

    public Task<bool> TryAddUserAsync(User user, CancellationToken ct) => TryAddAsync(_users, ToEntity(user), ct);

    public Task SetTokensValidAfterAsync(string userId, DateTime cutoff, CancellationToken ct) =>
        MergeIfExistsAsync(_users, new TableEntity(userId, UserRow) { ["TokensValidAfter"] = Utc(cutoff) }, ct);

    public Task DeleteUserAsync(string userId, CancellationToken ct) => DeleteAsync(_users, userId, UserRow, ct);

    // ───────── Albums ─────────

    private static TableEntity ToEntity(Album a) => new(a.OwnerId, Key(a.Id))
    {
        ["Name"] = a.Name,
        ["Description"] = a.Description,
        ["CreatedAt"] = Utc(a.CreatedAt),
        ["UpdatedAt"] = Utc(a.UpdatedAt),
        ["ContentVersion"] = a.ContentVersion,
        ["CompiledVersion"] = a.CompiledVersion,
        ["CompiledAt"] = Utc(a.CompiledAt),
        ["ShareToken"] = a.ShareToken,
        ["ShareHideMap"] = a.ShareHideMap,
    };

    private static Album ToAlbum(TableEntity e) => new()
    {
        Id = Guid.ParseExact(e.RowKey, "N"),
        OwnerId = e.PartitionKey,
        Name = e.GetString("Name") ?? "",
        Description = e.GetString("Description"),
        CreatedAt = Utc(e, "CreatedAt") ?? default,
        UpdatedAt = Utc(e, "UpdatedAt") ?? default,
        ContentVersion = e.GetInt32("ContentVersion") ?? 0,
        CompiledVersion = e.GetInt32("CompiledVersion"),
        CompiledAt = Utc(e, "CompiledAt"),
        ShareToken = e.GetString("ShareToken"),
        ShareHideMap = e.GetBoolean("ShareHideMap") ?? false,
        ETag = e.ETag.ToString(),
    };

    public async Task<IReadOnlyList<Album>> ListAlbumsAsync(string ownerId, CancellationToken ct) =>
        (await QueryAsync(_albums, TableClient.CreateQueryFilter($"PartitionKey eq {ownerId}"), ct)).Select(ToAlbum).ToList();

    public async Task<Album?> GetAlbumAsync(string ownerId, Guid albumId, CancellationToken ct) =>
        await GetAsync(_albums, ownerId, Key(albumId), ct) is { } e ? ToAlbum(e) : null;

    public async Task AddAlbumAsync(Album album, CancellationToken ct)
    {
        var r = await _albums.AddEntityAsync(ToEntity(album), ct);
        album.ETag = r.Headers.ETag?.ToString();
    }

    public async Task<bool> TryUpdateAlbumAsync(Album album, CancellationToken ct)
    {
        if (album.ETag is null) return false;
        try
        {
            var r = await _albums.UpdateEntityAsync(ToEntity(album), new ETag(album.ETag), TableUpdateMode.Replace, ct);
            album.ETag = r.Headers.ETag?.ToString();
            return true;
        }
        catch (RequestFailedException e) when (Is(e, 412) || Is(e, 404)) { return false; } // changed meanwhile, or gone
    }

    public Task<bool> DeleteAlbumAsync(string ownerId, Guid albumId, CancellationToken ct) => DeleteAsync(_albums, ownerId, Key(albumId), ct);

    // ───────── Media ─────────

    private static TableEntity ToEntity(MediaFile m) => new(m.OwnerId, MediaRow(m.AlbumId, m.Id))
    {
        ["AlbumId"] = m.AlbumId,
        ["MediaId"] = m.Id,
        ["Collection"] = (int)m.Collection,
        ["Kind"] = (int)m.Kind,
        ["FileName"] = m.FileName,
        ["ContentType"] = m.ContentType,
        ["SizeBytes"] = m.SizeBytes,
        ["BlobName"] = m.BlobName,
        ["UploadedAt"] = Utc(m.UploadedAt),
        // Local wall-clock time with no zone (like EXIF): stored as text so nothing converts it.
        ["TakenAt"] = m.TakenAt?.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        ["TakenSource"] = (int?)m.TakenSource,
        ["Latitude"] = m.Latitude,
        ["Longitude"] = m.Longitude,
        ["ThumbnailState"] = (int)m.ThumbnailState,
    };

    private static MediaFile ToMedia(TableEntity e) => new()
    {
        Id = e.GetGuid("MediaId") ?? default,
        AlbumId = e.GetGuid("AlbumId") ?? default,
        OwnerId = e.PartitionKey,
        Collection = (MediaCollection)(e.GetInt32("Collection") ?? 0),
        Kind = (MediaKind)(e.GetInt32("Kind") ?? 0),
        FileName = e.GetString("FileName") ?? "",
        ContentType = e.GetString("ContentType") ?? "application/octet-stream",
        SizeBytes = e.GetInt64("SizeBytes") ?? 0,
        BlobName = e.GetString("BlobName") ?? "",
        UploadedAt = Utc(e, "UploadedAt") ?? default,
        TakenAt = e.GetString("TakenAt") is { } t ? DateTime.ParseExact(t, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) : null,
        TakenSource = (TakenSource?)e.GetInt32("TakenSource"),
        Latitude = e.GetDouble("Latitude"),
        Longitude = e.GetDouble("Longitude"),
        ThumbnailState = (ThumbnailState)(e.GetInt32("ThumbnailState") ?? 0),
    };

    /// <summary>The owner's partition, or the slice of it whose row keys start with "{album}_".</summary>
    private static string MediaFilter(string ownerId, Guid? albumId) => albumId is { } a
        ? TableClient.CreateQueryFilter($"PartitionKey eq {ownerId} and RowKey ge {Key(a) + "_"} and RowKey lt {Key(a) + "`"}") // '`' follows '_'
        : TableClient.CreateQueryFilter($"PartitionKey eq {ownerId}");

    public async Task<IReadOnlyList<MediaFile>> ListMediaAsync(string ownerId, Guid? albumId, CancellationToken ct) =>
        (await QueryAsync(_media, MediaFilter(ownerId, albumId), ct)).Select(ToMedia).ToList();

    public async Task<MediaFile?> GetMediaAsync(string ownerId, Guid albumId, Guid mediaId, CancellationToken ct) =>
        await GetAsync(_media, ownerId, MediaRow(albumId, mediaId), ct) is { } e ? ToMedia(e) : null;

    public async Task AddMediaAsync(MediaFile media, CancellationToken ct) => await _media.AddEntityAsync(ToEntity(media), ct);

    public Task<bool> DeleteMediaAsync(string ownerId, Guid albumId, Guid mediaId, CancellationToken ct) =>
        DeleteAsync(_media, ownerId, MediaRow(albumId, mediaId), ct);

    public async Task DeleteAllMediaAsync(string ownerId, Guid? albumId, CancellationToken ct)
    {
        var rows = await QueryAsync(_media, MediaFilter(ownerId, albumId), ct, select: ["PartitionKey", "RowKey"]);
        foreach (var chunk in rows.Chunk(100)) // a transaction holds at most 100 rows of one partition
            await _media.SubmitTransactionAsync(chunk.Select(e => new TableTransactionAction(TableTransactionActionType.Delete, e, ETag.All)), ct);
    }

    public Task SetThumbnailStateAsync(string ownerId, Guid albumId, Guid mediaId, ThumbnailState state, CancellationToken ct) =>
        MergeIfExistsAsync(_media, new TableEntity(ownerId, MediaRow(albumId, mediaId)) { ["ThumbnailState"] = (int)state }, ct);

    // ───────── Shares ─────────

    public async Task<(string OwnerId, Guid AlbumId)?> ResolveShareAsync(string token, CancellationToken ct) =>
        await GetAsync(_shares, token, ShareRow, ct) is { } e && e.GetString("OwnerId") is { } owner && e.GetGuid("AlbumId") is { } album
            ? (owner, album) : null;

    public Task<bool> TryAddShareAsync(string token, string ownerId, Guid albumId, CancellationToken ct) =>
        TryAddAsync(_shares, new TableEntity(token, ShareRow) { ["OwnerId"] = ownerId, ["AlbumId"] = albumId }, ct);

    public Task DeleteShareAsync(string token, CancellationToken ct) => DeleteAsync(_shares, token, ShareRow, ct);
}
