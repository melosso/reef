using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;

namespace Reef.Core.Workflows.Conversion;

// Reads profile rows for the one time workflow conversion
public class LegacyProfileReader(DatabaseConfig config)
{
    public async Task<bool> ExistsAsync(CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        return await db.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'Profiles')", cancellationToken: ct));
    }

    public async Task<List<Profile>> ExportsAsync(int? id = null, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        return [.. await db.QueryAsync<Profile>(new CommandDefinition("SELECT * FROM Profiles WHERE @id IS NULL OR Id = @id ORDER BY Id", new { id }, cancellationToken: ct))];
    }

    public async Task<List<ImportProfile>> ImportsAsync(int? id = null, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        return [.. await db.QueryAsync<ImportProfile>(new CommandDefinition("SELECT * FROM ImportProfiles WHERE @id IS NULL OR Id = @id ORDER BY Id", new { id }, cancellationToken: ct))];
    }

    public async Task<List<ImportOutputTarget>> OutputTargetsAsync(int importProfileId, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        return [.. await db.QueryAsync<ImportOutputTarget>(new CommandDefinition("""
            SELECT iot.*, d.Name AS DestinationName, CAST(d.Type AS TEXT) AS DestinationType
            FROM ImportOutputTargets iot LEFT JOIN Destinations d ON iot.DestinationId = d.Id
            WHERE iot.ImportProfileId = @importProfileId ORDER BY iot.SortOrder, iot.Id
            """, new { importProfileId }, cancellationToken: ct))];
    }

    public async Task<List<Job>> JobsAsync(string column, int id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        return [.. await db.QueryAsync<Job>(new CommandDefinition($"SELECT * FROM Jobs WHERE {column} = @id", new { id }, cancellationToken: ct))];
    }
}
