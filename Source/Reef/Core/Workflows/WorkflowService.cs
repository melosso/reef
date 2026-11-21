using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Nodes.Exceptions;
using Reef.Core.Workflows.Catalogs;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows;

public class WorkflowService(DatabaseConfig config)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<List<WorkflowDocument>> ListAsync(CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        var rows = await db.QueryAsync<WorkflowRow>(new CommandDefinition("SELECT Id, Name, Version, DocumentJson, UpdatedAt FROM Workflows ORDER BY Name", cancellationToken: ct));
        return [.. rows.Select(ToDocument)];
    }

    public async Task<WorkflowDocument?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        var row = await db.QuerySingleOrDefaultAsync<WorkflowRow>(new CommandDefinition(
            "SELECT Id, Name, Version, DocumentJson, UpdatedAt FROM Workflows WHERE Id = @id", new { id }, cancellationToken: ct));
        return row is null ? null : ToDocument(row);
    }

    public async Task<WorkflowDocument> SaveAsync(int? id, WorkflowDocument document, int? userId, CancellationToken ct = default)
    {
        var errors = WorkflowValidator.Validate(WorkflowNodeCatalog.Catalog, document);
        if (errors.Count > 0)
            throw new NodeValidationException(errors);

        var json = JsonSerializer.Serialize(new { document.Nodes, document.Edges, document.MaxPasses }, Json);
        var values = new { id, Name = document.Name.Trim(), DocumentJson = json, Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))),
            Now = DateTime.UtcNow.ToString("O"), userId };

        await using var db = new SqliteConnection(config.ConnectionString);
        if (id is null)
            id = await db.ExecuteScalarAsync<int>(new CommandDefinition("""
                INSERT INTO Workflows (Name, DocumentJson, Version, Hash, CreatedAt, UpdatedAt, CreatedBy)
                VALUES (@Name, @DocumentJson, 1, @Hash, @Now, @Now, @userId) RETURNING Id
                """, values, cancellationToken: ct));
        else if (await db.ExecuteAsync(new CommandDefinition("""
                UPDATE Workflows SET Name = @Name, DocumentJson = @DocumentJson, Hash = @Hash, UpdatedAt = @Now,
                    Version = Version + CASE WHEN Hash = @Hash AND Name = @Name THEN 0 ELSE 1 END
                WHERE Id = @id
                """, values, cancellationToken: ct)) == 0)
            throw new KeyNotFoundException("Workflow not found");

        return await GetAsync(id.Value, ct) ?? throw new InvalidOperationException("Workflow was saved but could not be read back");
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        return await db.ExecuteAsync(new CommandDefinition("""
            DELETE FROM NodeRuns WHERE WorkflowExecutionId IN (SELECT Id FROM WorkflowExecutions WHERE WorkflowId = @id);
            DELETE FROM WorkflowExecutions WHERE WorkflowId = @id;
            DELETE FROM DeltaSyncState WHERE Scope LIKE 'workflow:' || @id || ':%';
            DELETE FROM Workflows WHERE Id = @id;
            """, new { id }, cancellationToken: ct)) > 0;
    }

    private static WorkflowDocument ToDocument(WorkflowRow row)
    {
        var graph = JsonSerializer.Deserialize<WorkflowDocument>(row.DocumentJson, Json) ?? new WorkflowDocument();
        graph.Id = (int)row.Id;
        graph.Name = row.Name;
        graph.Version = (int)row.Version;
        graph.UpdatedAt = DateTime.Parse(row.UpdatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
        return graph;
    }
}
