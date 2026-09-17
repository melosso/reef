using FluentAssertions;
using Reef.Core.Database;
using Reef.Core.Models;

namespace Reef.Tests.Database;

public class ProfileGraphBackfillMigrationTests
{
    private static Profile PlainProfile() => new()
    {
        Id = 1,
        Name = "plain",
        ConnectionId = 1,
        Query = "SELECT 1",
        Hash = "test-hash",
    };

    [Fact]
    public void Build_PlainQueryProfile_YieldsSourceThenDestinationOnly()
    {
        var graph = ProfileGraphBackfillMigration.Build(PlainProfile());

        graph.Nodes.Select(n => n.Type).Should().Equal("source", "destination");
        graph.Edges.Should().ContainSingle(e => e.From == "source" && e.To == "destination");
    }

    [Fact]
    public void Build_PreProcessAndDeltaSyncProfile_PreservesRealExecutionOrder()
    {
        var profile = PlainProfile();
        profile.PreProcessType = "Query";
        profile.PreProcessConfig = "{\"type\":\"Query\",\"command\":\"UPDATE x SET y = 1\"}";
        profile.DeltaSyncEnabled = true;
        profile.DeltaSyncReefIdColumn = "Id";

        var graph = ProfileGraphBackfillMigration.Build(profile);

        graph.Nodes.Select(n => n.Type).Should().Equal("preprocess", "source", "deltasync", "destination");
        graph.Edges.Select(e => (e.From, e.To)).Should().Equal(
            ("preprocess", "source"),
            ("source", "deltasync"),
            ("deltasync", "destination"));
    }

    [Fact]
    public void Build_EmailExportProfile_OmitsDeadSplitTemplateAndDestinationNodes()
    {
        // emailexport always returns before reaching splitoutput/template/destination in ExecuteProfileAsync,
        // and email grouping goes through EmailGroupBySplitKey inside the emailexport node's own config, not a splitoutput node
        var profile = PlainProfile();
        profile.IsEmailExport = true;
        profile.EmailTemplateId = 5;
        profile.SplitEnabled = true;
        profile.SplitKeyColumn = "Region";
        profile.TemplateId = 9;
        profile.PostProcessType = "Webhook";
        profile.PostProcessConfig = "{\"type\":\"Webhook\",\"command\":\"https://example.com/hook\"}";

        var graph = ProfileGraphBackfillMigration.Build(profile);

        graph.Nodes.Select(n => n.Type).Should().Equal("source", "emailexport", "postprocess");
    }

    [Fact]
    public void Build_SplitProfile_KeepsTemplateAndDestinationAlongsideSplitoutput()
    {
        var profile = PlainProfile();
        profile.SplitEnabled = true;
        profile.SplitKeyColumn = "Region";
        profile.TemplateId = 9;

        var graph = ProfileGraphBackfillMigration.Build(profile);

        graph.Nodes.Select(n => n.Type).Should().Equal("source", "splitoutput", "template", "destination");
    }

    [Fact]
    public void Build_AlwaysProducesADag_NoCycles()
    {
        var profile = PlainProfile();
        profile.PreProcessType = "Query";
        profile.DeltaSyncEnabled = true;
        profile.DeltaSyncReefIdColumn = "Id";
        profile.PostProcessType = "Query";

        var graph = ProfileGraphBackfillMigration.Build(profile);

        var nodeIds = graph.Nodes.Select(n => n.Id).ToHashSet();
        graph.Edges.Should().OnlyContain(e => nodeIds.Contains(e.From) && nodeIds.Contains(e.To));
        graph.Edges.Select(e => e.To).Should().OnlyHaveUniqueItems("each node has at most one incoming edge in a linear pipeline");
    }
}
