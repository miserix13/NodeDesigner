using NodeDesigner.Models.Graph;
using NodeDesigner.Services.Designer;

namespace NodeDesigner.Tests;

public sealed class GraphDocumentStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsGraphDocument()
    {
        var store = new GraphDocumentStore();
        var expected = TestGraphFactory.CreateSampleDocument();
        var path = CreateTempGraphPath();

        try
        {
            await store.SaveAsync(path, expected);
            var actual = await store.LoadAsync(path);

            AssertEquivalent(expected, actual);
        }
        finally
        {
            CleanupPath(path);
        }
    }

    [Fact]
    public async Task Load_WhenFileIsMissing_ReturnsEmptyDocument()
    {
        var store = new GraphDocumentStore();
        var path = CreateTempGraphPath();

        try
        {
            var loaded = await store.LoadAsync(path);

            Assert.Equal(GraphDocument.Empty, loaded);
        }
        finally
        {
            CleanupPath(path);
        }
    }

    [Fact]
    public async Task Load_WhenLegacyVersionAndInvalidZoom_NormalizesValues()
    {
        var store = new GraphDocumentStore();
        var path = CreateTempGraphPath();

        const string legacyJson = """
        {
          "version": 0,
          "viewport": { "offsetX": 7.25, "offsetY": -3.5, "zoom": 0 },
          "nodes": [
            {
              "id": "legacy_node",
              "kind": "processor",
              "position": { "x": 10, "y": 20 },
              "properties": { "name": "Legacy" },
              "ports": [
                { "id": "legacy_in", "name": "In", "direction": 0 },
                { "id": "legacy_out", "name": "Out", "direction": 1 }
              ]
            }
          ],
          "edges": [],
          "selectedNodeIds": ["legacy_node"]
        }
        """;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, legacyJson);

            var loaded = await store.LoadAsync(path);

            Assert.Equal(1, loaded.Version);
            Assert.Equal(7.25, loaded.Viewport.OffsetX, 3);
            Assert.Equal(-3.5, loaded.Viewport.OffsetY, 3);
            Assert.Equal(1, loaded.Viewport.Zoom, 3);
            Assert.Single(loaded.Nodes);
            Assert.Contains("legacy_node", loaded.SelectedNodeIds);
        }
        finally
        {
            CleanupPath(path);
        }
    }

    private static void AssertEquivalent(GraphDocument expected, GraphDocument actual)
    {
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Viewport.OffsetX, actual.Viewport.OffsetX, 5);
        Assert.Equal(expected.Viewport.OffsetY, actual.Viewport.OffsetY, 5);
        Assert.Equal(expected.Viewport.Zoom, actual.Viewport.Zoom, 5);

        Assert.Equal(expected.SelectedNodeIds.OrderBy(value => value), actual.SelectedNodeIds.OrderBy(value => value));

        Assert.Equal(expected.Nodes.Length, actual.Nodes.Length);

        for (var nodeIndex = 0; nodeIndex < expected.Nodes.Length; nodeIndex++)
        {
            var expectedNode = expected.Nodes[nodeIndex];
            var actualNode = actual.Nodes[nodeIndex];

            Assert.Equal(expectedNode.Id, actualNode.Id);
            Assert.Equal(expectedNode.Kind, actualNode.Kind);
            Assert.Equal(expectedNode.Position.X, actualNode.Position.X, 5);
            Assert.Equal(expectedNode.Position.Y, actualNode.Position.Y, 5);
            Assert.Equal(expectedNode.Properties.Count, actualNode.Properties.Count);
            Assert.Equal(expectedNode.Ports.Length, actualNode.Ports.Length);

            foreach (var expectedProperty in expectedNode.Properties)
            {
                Assert.True(actualNode.Properties.TryGetValue(expectedProperty.Key, out var actualValue));
                Assert.Equal(expectedProperty.Value, actualValue);
            }

            for (var portIndex = 0; portIndex < expectedNode.Ports.Length; portIndex++)
            {
                var expectedPort = expectedNode.Ports[portIndex];
                var actualPort = actualNode.Ports[portIndex];

                Assert.Equal(expectedPort.Id, actualPort.Id);
                Assert.Equal(expectedPort.Name, actualPort.Name);
                Assert.Equal(expectedPort.Direction, actualPort.Direction);
            }
        }

        Assert.Equal(expected.Edges.Length, actual.Edges.Length);

        for (var edgeIndex = 0; edgeIndex < expected.Edges.Length; edgeIndex++)
        {
            var expectedEdge = expected.Edges[edgeIndex];
            var actualEdge = actual.Edges[edgeIndex];

            Assert.Equal(expectedEdge.Id, actualEdge.Id);
            Assert.Equal(expectedEdge.FromNodeId, actualEdge.FromNodeId);
            Assert.Equal(expectedEdge.FromPortId, actualEdge.FromPortId);
            Assert.Equal(expectedEdge.ToNodeId, actualEdge.ToNodeId);
            Assert.Equal(expectedEdge.ToPortId, actualEdge.ToPortId);
        }
    }

    private static string CreateTempGraphPath()
    {
        return Path.Combine(Path.GetTempPath(), "NodeDesigner.Tests", Guid.NewGuid().ToString("N"), "graph.json");
    }

    private static void CleanupPath(string path)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
