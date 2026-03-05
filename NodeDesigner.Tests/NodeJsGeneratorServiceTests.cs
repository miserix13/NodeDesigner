using System.Collections.Immutable;
using NodeDesigner.Models.Graph;
using NodeDesigner.Services.Designer;

namespace NodeDesigner.Tests;

public sealed class NodeJsGeneratorServiceTests
{
    [Fact]
    public void Generate_WhenGraphIsEmpty_EmitsEmptyComment()
    {
        var generator = new NodeJsGeneratorService();

        var output = generator.Generate(GraphDocument.Empty);

        Assert.Contains("// Graph is empty.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_IsDeterministic_ForSameDocument()
    {
        var generator = new NodeJsGeneratorService();
        var document = TestGraphFactory.CreateSampleDocument();

        var firstOutput = generator.Generate(document);
        var secondOutput = generator.Generate(document);

        Assert.Equal(firstOutput, secondOutput);
    }

    [Fact]
    public void Generate_EscapesSingleQuoteAndBackslashValues()
    {
        var generator = new NodeJsGeneratorService();

        var node = new GraphNode(
            Id: "node'\\alpha",
            Kind: "processor",
            Position: new GraphPosition(1, 2),
            Properties: ImmutableDictionary<string, string>.Empty
                .WithComparers(StringComparer.Ordinal)
                .Add("name", "A\\B 'C'"),
            Ports: ImmutableArray.Create(
                new GraphPort("port_in", "In", GraphPortDirection.Input),
                new GraphPort("port_out", "Out", GraphPortDirection.Output))
        );

        var document = new GraphDocument(
            Version: 1,
            Viewport: GraphViewport.Default,
            Nodes: ImmutableArray.Create(node),
            Edges: ImmutableArray<GraphEdge>.Empty,
            SelectedNodeIds: ImmutableHashSet<string>.Empty);

        var output = generator.Generate(document);

        Assert.Contains("id: 'node\\'\\\\alpha'", output, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"A\\\\B", output, StringComparison.Ordinal);
        Assert.True(
            output.Contains("\"name\":\"A\\\\B 'C'\"", StringComparison.Ordinal)
            || output.Contains("\"name\":\"A\\\\B \\u0027C\\u0027\"", StringComparison.Ordinal),
            "Expected property serialization to preserve apostrophe content using literal or unicode-escaped form.");
    }
}
