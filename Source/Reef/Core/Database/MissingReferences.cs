using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.Sqlite;
using Serilog;

namespace Reef.Core.Database;

// Drops foreign keys to missing tables
public static partial class MissingReferences
{
    public static async Task RemoveAsync(SqliteConnection connection)
    {
        var tables = (await connection.QueryAsync<(string Name, string Sql)>("SELECT name, sql FROM sqlite_master WHERE type = 'table' AND sql IS NOT NULL AND name NOT LIKE 'sqlite_%'")).ToList();
        var existing = tables.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, sql) in tables)
        {
            var missing = References().Matches(sql).Select(m => m.Groups["table"].Value).Where(t => !existing.Contains(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count > 0)
                await RebuildAsync(connection, name, sql, missing);
        }
    }

    private static async Task RebuildAsync(SqliteConnection connection, string name, string sql, List<string> missing)
    {
        foreach (var table in missing)
        {
            var target = Regex.Escape(table);
            sql = Regex.Replace(sql, $@",\s*FOREIGN\s+KEY\s*\([^)]*\)\s*REFERENCES\s+""?{target}""?\s*\([^)]*\)({Actions})*", "", RegexOptions.IgnoreCase);
            sql = Regex.Replace(sql, $@"\s+REFERENCES\s+""?{target}""?\s*\([^)]*\)({Actions})*", "", RegexOptions.IgnoreCase);
        }
        var temp = $"{name}_rebuild";
        var create = Regex.Replace(sql, $@"^CREATE\s+TABLE\s+(IF\s+NOT\s+EXISTS\s+)?""?{Regex.Escape(name)}""?", $"CREATE TABLE \"{temp}\"", RegexOptions.IgnoreCase);
        var columns = string.Join(", ", (await connection.QueryAsync<string>($"SELECT name FROM pragma_table_info('{name}')")).Select(c => $"\"{c}\""));
        var extras = (await connection.QueryAsync<string>("SELECT sql FROM sqlite_master WHERE type IN ('index', 'trigger') AND tbl_name = @name AND sql IS NOT NULL", new { name })).ToList();

        await connection.ExecuteAsync("PRAGMA foreign_keys = OFF");
        try
        {
            await using var tx = await connection.BeginTransactionAsync();
            await connection.ExecuteAsync(create, transaction: tx);
            await connection.ExecuteAsync($"INSERT INTO \"{temp}\" ({columns}) SELECT {columns} FROM \"{name}\"", transaction: tx);
            await connection.ExecuteAsync($"DROP TABLE \"{name}\"", transaction: tx);
            await connection.ExecuteAsync($"ALTER TABLE \"{temp}\" RENAME TO \"{name}\"", transaction: tx);
            foreach (var extra in extras)
                await connection.ExecuteAsync(extra, transaction: tx);
            await tx.CommitAsync();
        }
        finally
        {
            await connection.ExecuteAsync("PRAGMA foreign_keys = ON");
        }
        Log.Information("Removed references to {Tables} from {Table}", string.Join(", ", missing), name);
    }

    private const string Actions = @"\s+ON\s+(DELETE|UPDATE)\s+(SET\s+NULL|SET\s+DEFAULT|CASCADE|RESTRICT|NO\s+ACTION)";

    [GeneratedRegex(@"REFERENCES\s+""?(?<table>\w+)""?\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex References();
}
