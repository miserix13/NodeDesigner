using System.Collections.Immutable;
using NodeDesigner.Models.Graph;

namespace NodeDesigner.Tests;

internal static class TestGraphFactory
{
    public static GraphDocument CreateSampleDocument()
    {
        const string firstNodeId = "node_alpha";
        const string secondNodeId = "node_beta";

        var firstNode = new GraphNode(
            Id: firstNodeId,
            Kind: "processor",
            Position: new GraphPosition(24.5, 12.25),
            Properties: ImmutableDictionary<string, string>.Empty
                .WithComparers(StringComparer.Ordinal)
                .Add("name", "Alpha")
                .Add("category", "entry"),
            Ports: ImmutableArray.Create(
                new GraphPort($"{firstNodeId}_in", "In", GraphPortDirection.Input),
                new GraphPort($"{firstNodeId}_out", "Out", GraphPortDirection.Output))
        );

        var secondNode = new GraphNode(
            Id: secondNodeId,
            Kind: "processor",
            Position: new GraphPosition(312.75, 188.125),
            Properties: ImmutableDictionary<string, string>.Empty
                .WithComparers(StringComparer.Ordinal)
                .Add("name", "Beta")
                .Add("category", "transform"),
            Ports: ImmutableArray.Create(
                new GraphPort($"{secondNodeId}_in", "In", GraphPortDirection.Input),
                new GraphPort($"{secondNodeId}_out", "Out", GraphPortDirection.Output))
        );

        var edge = new GraphEdge(
            Id: "edge_alpha_beta",
            FromNodeId: firstNodeId,
            FromPortId: $"{firstNodeId}_out",
            ToNodeId: secondNodeId,
            ToPortId: $"{secondNodeId}_in");

        return new GraphDocument(
            Version: 2,
            Viewport: new GraphViewport(16.5, -8.25, 1.35),
            Nodes: ImmutableArray.Create(firstNode, secondNode),
            Edges: ImmutableArray.Create(edge),
            SelectedNodeIds: ImmutableHashSet.Create(StringComparer.Ordinal, secondNodeId));
    }
}
