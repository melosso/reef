using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Reef.Core.PublicIds;

// Translates between the integer identity Reef uses internally and the Ulid the api exposes.
public sealed partial class PublicIdMap(string connectionString)
{
    // shortcut: entries are never evicted, bound the cache when execution tables reach millions of rows
    private readonly ConcurrentDictionary<(string Table, long Id), string> _ulids = new();
    private readonly ConcurrentDictionary<(string Table, string Ulid), long> _ids = new();

    public static bool IsUlid(string? value) => value is not null && UlidPattern().IsMatch(value);

    public string? ToUlid(string table, long id)
    {
        if (_ulids.TryGetValue((table, id), out var cached)) return cached;
        using var db = new SqliteConnection(connectionString);
        var ulid = db.ExecuteScalar<string?>($"SELECT Ulid FROM \"{PublicIdTables.Known(table)}\" WHERE Id = @id", new { id });
        if (ulid is not null) Remember(table, id, ulid);
        return ulid;
    }

    public bool TryToId(string table, string? ulid, out long id)
    {
        id = 0;
        if (!IsUlid(ulid)) return false;
        ulid = ulid!.ToUpperInvariant();
        if (_ids.TryGetValue((table, ulid), out id)) return true;
        using var db = new SqliteConnection(connectionString);
        var found = db.ExecuteScalar<long?>($"SELECT Id FROM \"{PublicIdTables.Known(table)}\" WHERE Ulid = @ulid", new { ulid });
        if (found is null) return false;
        Remember(table, found.Value, ulid);
        id = found.Value;
        return true;
    }

    private void Remember(string table, long id, string ulid)
    {
        _ulids[(table, id)] = ulid;
        _ids[(table, ulid)] = id;
    }

    [GeneratedRegex("^[0-9A-HJKMNP-TV-Za-hjkmnp-tv-z]{26}$")]
    private static partial Regex UlidPattern();
}
