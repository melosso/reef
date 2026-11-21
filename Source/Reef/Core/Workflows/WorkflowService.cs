using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Reef.Core.Nodes;
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

        var stored = id is null ? null : await GetAsync(id.Value, ct);
        foreach (var node in document.Nodes.Where(n => n.Type == WorkflowNodeCatalog.Webhook))
        {
            node.Config ??= new JsonObject();
            node.Config.Remove(WorkflowNodeCatalog.TokenHash);
            if (stored?.Nodes.FirstOrDefault(n => n.Id == node.Id && n.Type == WorkflowNodeCatalog.Webhook)?.Config?[WorkflowNodeCatalog.TokenHash]?.DeepClone() is { } hash)
                node.Config[WorkflowNodeCatalog.TokenHash] = hash;
        }

        var json = Serialize(document);
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

    public async Task SetWebhookTokenHashAsync(int id, string nodeId, string hash, CancellationToken ct)
    {
        var document = await GetAsync(id, ct) ?? throw new KeyNotFoundException("Workflow not found");
        var node = document.Nodes.FirstOrDefault(n => n.Id == nodeId && n.Type == WorkflowNodeCatalog.Webhook) ?? throw new KeyNotFoundException("Webhook node not found");
        (node.Config ??= new JsonObject())[WorkflowNodeCatalog.TokenHash] = hash;
        await using var db = new SqliteConnection(config.ConnectionString);
        await db.ExecuteAsync(new CommandDefinition("UPDATE Workflows SET DocumentJson = @json WHERE Id = @id", new { id, json = Serialize(document) }, cancellationToken: ct));
    }

    // shortcut scans every workflow, index the hash when webhooks grow past hundreds
    public async Task<(WorkflowDocument Workflow, WorkflowNode Node)?> FindWebhookAsync(string hash, CancellationToken ct)
    {
        foreach (var workflow in await ListAsync(ct))
        {
            if (workflow.Nodes.FirstOrDefault(n => n.Type == WorkflowNodeCatalog.Webhook && ConfigPath.ToText(n.Config?[WorkflowNodeCatalog.TokenHash]) == hash) is { } node)
                return (workflow, node);
        }
        return null;
    }

    private static string Serialize(WorkflowDocument document) => JsonSerializer.Serialize(new { document.Nodes, document.Edges, document.MaxPasses, document.Active }, Json);

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
