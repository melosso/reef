using FluentAssertions;
using Reef.Core.Models;
using Reef.Core.Services;

namespace Reef.Tests.Services;

public class ProfilePipelineValidatorTests
{
    private static ProfileGraphNode Node(string id, string type) => new() { Id = id, Type = type };

    private static ProfileGraph Chain(params ProfileGraphNode[] nodes)
    {
        var graph = new ProfileGraph { Nodes = nodes.ToList() };
        for (var i = 1; i < nodes.Length; i++)
            graph.Edges.Add(new ProfileGraphEdge { From = nodes[i - 1].Id, To = nodes[i].Id });
        return graph;
    }

    [Fact]
    public void Validate_PlainSourceAndDestination_IsValid()
    {
        var graph = Chain(Node("source", "source"), Node("destination", "destination"));

        ProfilePipelineValidator.Validate(graph).Should().BeNull();
    }

    [Fact]
    public void Validate_EmailExportAlone_IsValid()
    {
        var graph = Chain(Node("source", "source"), Node("emailexport", "emailexport"));

        ProfilePipelineValidator.Validate(graph).Should().BeNull();
    }

    [Fact]
    public void Validate_SplitWithTemplateAndDestination_IsValid()
    {
        var graph = Chain(
            Node("source", "source"),
            Node("splitoutput", "splitoutput"),
            Node("template", "template"),
            Node("destination", "destination"));

        ProfilePipelineValidator.Validate(graph).Should().BeNull();
    }

    [Fact]
    public void Validate_EmailAndDestinationTogether_IsRejected()
    {
        var graph = Chain(Node("source", "source"), Node("emailexport", "emailexport"), Node("destination", "destination"));

        ProfilePipelineValidator.Validate(graph).Should().Contain("alternate output strategies");
    }

    [Fact]
    public void Validate_NeitherEmailNorDestination_IsRejected()
    {
        var graph = Chain(Node("source", "source"), Node("preprocess", "preprocess"));

        ProfilePipelineValidator.Validate(graph).Should().Contain("output strategy");
    }

    [Fact]
    public void Validate_SplitWithoutDestination_IsRejected()
    {
        var graph = Chain(Node("source", "source"), Node("emailexport", "emailexport"), Node("splitoutput", "splitoutput"));

        ProfilePipelineValidator.Validate(graph).Should().NotBeNull();
    }

    [Fact]
    public void Validate_TemplateWithEmail_IsRejected()
    {
        var graph = Chain(Node("source", "source"), Node("emailexport", "emailexport"), Node("template", "template"));

        ProfilePipelineValidator.Validate(graph).Should().Contain("not used with emailexport");
    }

    [Fact]
    public void Validate_MissingSource_IsRejected()
    {
        var graph = Chain(Node("destination", "destination"));

        ProfilePipelineValidator.Validate(graph).Should().Contain("exactly one source");
    }

    [Fact]
    public void Validate_DuplicateSource_IsRejected()
    {
        var graph = Chain(Node("source", "source"), Node("source2", "source"), Node("destination", "destination"));

        ProfilePipelineValidator.Validate(graph).Should().Contain("exactly one source");
    }

    [Fact]
    public void Validate_Cycle_IsRejected()
    {
        var graph = new ProfileGraph
        {
            Nodes = new() { Node("source", "source"), Node("destination", "destination") },
            Edges = new()
            {
                new() { From = "source", To = "destination" },
                new() { From = "destination", To = "source" }
            }
        };

        ProfilePipelineValidator.Validate(graph).Should().Be("Graph contains a cycle");
    }
}
