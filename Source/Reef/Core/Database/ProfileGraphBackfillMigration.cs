using System.Text.Json;
using Microsoft.Data.Sqlite;
using Dapper;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Database;

// synthesizes a ProfileGraph from existing flat Profile fields for rows with no CanvasLayoutJson yet, in the same stage order ExecutionService.ExecuteProfileAsync runs them today
public class ProfileGraphBackfillMigration(string connectionString)
{
    public async Task ApplyAsync()
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        var profiles = await conn.QueryAsync<Profile>(
            "SELECT * FROM Profiles WHERE CanvasLayoutJson IS NULL OR CanvasLayoutJson = ''");

        var count = 0;
        foreach (var profile in profiles)
        {
            var graph = Build(profile);
            var json = JsonSerializer.Serialize(graph);
            await conn.ExecuteAsync(
                "UPDATE Profiles SET CanvasLayoutJson = @Json WHERE Id = @Id",
                new { Json = json, profile.Id });
            count++;
        }

        if (count > 0)
            Log.Debug("Backfilled canvas graph for {Count} profiles", count);

        Log.Debug("✓ Profile graph backfill migration completed");
    }

    public static ProfileGraph Build(Profile profile)
    {
        var graph = new ProfileGraph();
        double x = 0;
        const double stepX = 260;

        string? previousId = null;

        void AddNode(string id, string type, object config)
        {
            graph.Nodes.Add(new ProfileGraphNode { Id = id, Type = type, Config = config, X = x, Y = 0 });
            x += stepX;
            if (previousId != null)
                graph.Edges.Add(new ProfileGraphEdge { From = previousId, To = id });
            previousId = id;
        }

        if (!string.IsNullOrEmpty(profile.PreProcessType))
        {
            AddNode("preprocess", "preprocess", new
            {
                type = profile.PreProcessType,
                config = profile.PreProcessConfig,
                rollbackOnFailure = profile.PreProcessRollbackOnFailure,
            });
        }

        AddNode("source", "source", new
        {
            connectionId = profile.ConnectionId,
            query = profile.Query,
        });

        if (profile.DeltaSyncEnabled)
        {
            AddNode("deltasync", "deltasync", new
            {
                reefIdColumn = profile.DeltaSyncReefIdColumn,
                hashAlgorithm = profile.DeltaSyncHashAlgorithm,
                trackDeletes = profile.DeltaSyncTrackDeletes,
                retentionDays = profile.DeltaSyncRetentionDays,
                duplicateStrategy = profile.DeltaSyncDuplicateStrategy,
                nullStrategy = profile.DeltaSyncNullStrategy,
                resetOnSchemaChange = profile.DeltaSyncResetOnSchemaChange,
                numericPrecision = profile.DeltaSyncNumericPrecision,
                removeNonPrintable = profile.DeltaSyncRemoveNonPrintable,
                reefIdNormalization = profile.DeltaSyncReefIdNormalization,
            });
        }

        if (profile.IsEmailExport)
        {
            AddNode("emailexport", "emailexport", new
            {
                emailTemplateId = profile.EmailTemplateId,
                recipientsColumn = profile.EmailRecipientsColumn,
                recipientsHardcoded = profile.EmailRecipientsHardcoded,
                useHardcodedRecipients = profile.UseHardcodedRecipients,
                ccColumn = profile.EmailCcColumn,
                ccHardcoded = profile.EmailCcHardcoded,
                useHardcodedCc = profile.UseHardcodedCc,
                subjectColumn = profile.EmailSubjectColumn,
                subjectHardcoded = profile.EmailSubjectHardcoded,
                useHardcodedSubject = profile.UseHardcodedSubject,
                successThresholdPercent = profile.EmailSuccessThresholdPercent,
                attachmentConfig = profile.EmailAttachmentConfig,
                groupBySplitKey = profile.EmailGroupBySplitKey,
                approvalRequired = profile.EmailApprovalRequired,
                approvalRoles = profile.EmailApprovalRoles,
            });
        }

        if (profile.SplitEnabled && !profile.IsEmailExport)
        {
            AddNode("splitoutput", "splitoutput", new
            {
                keyColumn = profile.SplitKeyColumn,
                filenameTemplate = profile.SplitFilenameTemplate,
                batchSize = profile.SplitBatchSize,
                postProcessPerSplit = profile.PostProcessPerSplit,
            });
        }

        if (profile.TemplateId.HasValue && !profile.IsEmailExport)
        {
            AddNode("template", "template", new
            {
                templateId = profile.TemplateId,
                transformationOptionsJson = profile.TransformationOptionsJson,
            });
        }

        if (!profile.IsEmailExport)
        {
            AddNode("destination", "destination", new
            {
                outputFormat = profile.OutputFormat,
                destinationType = profile.OutputDestinationType,
                destinationConfig = profile.OutputDestinationConfig,
                destinationId = profile.OutputDestinationId,
                destinationEndpointId = profile.OutputDestinationEndpointId,
                filenameTemplate = profile.FilenameTemplate,
                excludeReefIdFromOutput = profile.ExcludeReefIdFromOutput,
                excludeSplitKeyFromOutput = profile.ExcludeSplitKeyFromOutput,
            });
        }

        if (!string.IsNullOrEmpty(profile.PostProcessType))
        {
            AddNode("postprocess", "postprocess", new
            {
                type = profile.PostProcessType,
                config = profile.PostProcessConfig,
                skipOnFailure = profile.PostProcessSkipOnFailure,
                rollbackOnFailure = profile.PostProcessRollbackOnFailure,
                onZeroRows = profile.PostProcessOnZeroRows,
            });
        }

        return graph;
    }
}
