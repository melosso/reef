using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace Reef.Core.Database;

// Gives every table a unique Ulid next to its integer identity, backfilled once and filled by a trigger on insert.
public class UlidMigration(string connectionString)
{
    private const string Alphabet = "'0123456789ABCDEFGHJKMNPQRSTVWXYZ'";
    private const string Milliseconds = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";

    internal static readonly string NewUlidSql = string.Join(" || ",
        Enumerable.Range(0, 10).Select(i => $"substr({Alphabet}, (({Milliseconds} >> {45 - 5 * i}) & 31) + 1, 1)")
            .Concat(Enumerable.Repeat($"substr({Alphabet}, (random() & 31) + 1, 1)", 16)));

    public async Task ApplyAsync()
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        var tables = await conn.QueryAsync<string>("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'");
        foreach (var table in tables)
        {
            var columns = (await conn.QueryAsync($"PRAGMA table_info(\"{table}\")"))
                .Select(r => (string)r.name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!columns.Contains("Ulid"))
            {
                await conn.ExecuteAsync($"ALTER TABLE \"{table}\" ADD COLUMN Ulid TEXT NULL");
                Log.Debug("Added column Ulid to {Table}", table);
            }

            await conn.ExecuteAsync($"UPDATE \"{table}\" SET Ulid = {NewUlidSql} WHERE Ulid IS NULL");
            await conn.ExecuteAsync($"CREATE UNIQUE INDEX IF NOT EXISTS \"IX_{table}_Ulid\" ON \"{table}\" (Ulid)");
            await conn.ExecuteAsync($"""
                CREATE TRIGGER IF NOT EXISTS "TR_{table}_Ulid" AFTER INSERT ON "{table}" FOR EACH ROW WHEN NEW.Ulid IS NULL
                BEGIN UPDATE "{table}" SET Ulid = {NewUlidSql} WHERE rowid = NEW.rowid; END
                """);
        }

        Log.Debug("Ulid migration completed");
    }
}
