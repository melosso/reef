using FluentAssertions;
using Reef.Core.Models;
using Reef.Core.Services;

namespace Reef.Tests.Services;

public class GraphExecutorTests
{
    private static ProfileGraphNode Node(string id) => new() { Id = id, Type = id };

    [Fact]
    public void Sort_LinearChain_OrdersSourceToDestination()
    {
        var graph = new ProfileGraph
        {
            Nodes = new() { Node("source"), Node("preprocess"), Node("destination") },
            Edges = new()
            {
                new() { From = "preprocess", To = "source" },
                new() { From = "source", To = "destination" }
            }
        };

        var (ordered, error) = GraphExecutor.Sort(graph);

        error.Should().BeNull();
        ordered.Select(n => n.Id).Should().Equal("preprocess", "source", "destination");
    }

    [Fact]
    public void Sort_DirectCycle_ReturnsError()
    {
        var graph = new ProfileGraph
        {
            Nodes = new() { Node("a"), Node("b") },
            Edges = new()
            {
                new() { From = "a", To = "b" },
                new() { From = "b", To = "a" }
            }
        };

        var (ordered, error) = GraphExecutor.Sort(graph);

        error.Should().Be("Graph contains a cycle");
        ordered.Should().BeEmpty();
    }

    [Fact]
    public void Sort_SelfLoop_ReturnsError()
    {
        var graph = new ProfileGraph
        {
            Nodes = new() { Node("a") },
            Edges = new() { new() { From = "a", To = "a" } }
        };

        var (_, error) = GraphExecutor.Sort(graph);

        error.Should().Be("Graph contains a cycle");
    }

    [Fact]
    public void Sort_EdgeToUnknownNode_ReturnsError()
    {
        var graph = new ProfileGraph
        {
            Nodes = new() { Node("a") },
            Edges = new() { new() { From = "a", To = "ghost" } }
        };

        var (_, error) = GraphExecutor.Sort(graph);

        error.Should().Be("Edge references unknown node: a -> ghost");
    }

    [Fact]
    public void Sort_DisconnectedNode_StillIncluded()
    {
        var graph = new ProfileGraph
        {
            Nodes = new() { Node("a"), Node("b"), Node("orphan") },
            Edges = new() { new() { From = "a", To = "b" } }
        };

        var (ordered, error) = GraphExecutor.Sort(graph);

        error.Should().BeNull();
        ordered.Select(n => n.Id).Should().Contain("orphan");
        ordered.Should().HaveCount(3);
    }
}
