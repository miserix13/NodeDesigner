using System.Text.Json;
using NodeDesigner.Models.Graph;

namespace NodeDesigner.Services.Designer;

public sealed class GraphDocumentStore : IGraphDocumentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task SaveAsync(string path, GraphDocument document, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var dto = GraphDocumentDto.FromDocument(document);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, dto, JsonOptions, cancellationToken);
    }

    public async Task<GraphDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return GraphDocument.Empty;
        }

        await using var stream = File.OpenRead(path);
        var dto = await JsonSerializer.DeserializeAsync<GraphDocumentDto>(stream, JsonOptions, cancellationToken);

        return dto?.ToDocument() ?? GraphDocument.Empty;
    }

    private sealed class GraphDocumentDto
    {
        public int Version { get; init; }

        public GraphViewportDto Viewport { get; init; } = new();

        public GraphNodeDto[] Nodes { get; init; } = [];

        public GraphEdgeDto[] Edges { get; init; } = [];

        public string[] SelectedNodeIds { get; init; } = [];

        public static GraphDocumentDto FromDocument(GraphDocument document)
        {
            return new GraphDocumentDto
            {
                Version = document.Version,
                Viewport = new GraphViewportDto
                {
                    OffsetX = document.Viewport.OffsetX,
                    OffsetY = document.Viewport.OffsetY,
                    Zoom = document.Viewport.Zoom,
                },
                Nodes = document.Nodes.Select(node => new GraphNodeDto
                {
                    Id = node.Id,
                    Kind = node.Kind,
                    Position = new GraphPositionDto
                    {
                        X = node.Position.X,
                        Y = node.Position.Y,
                    },
                    Properties = node.Properties.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal),
                    Ports = node.Ports.Select(port => new GraphPortDto
                    {
                        Id = port.Id,
                        Name = port.Name,
                        Direction = port.Direction,
                    }).ToArray(),
                }).ToArray(),
                Edges = document.Edges.Select(edge => new GraphEdgeDto
                {
                    Id = edge.Id,
                    FromNodeId = edge.FromNodeId,
                    FromPortId = edge.FromPortId,
                    ToNodeId = edge.ToNodeId,
                    ToPortId = edge.ToPortId,
                }).ToArray(),
                SelectedNodeIds = document.SelectedNodeIds.ToArray(),
            };
        }

        public GraphDocument ToDocument()
        {
            return new GraphDocument(
                Version: Version <= 0 ? 1 : Version,
                Viewport: new GraphViewport(
                    OffsetX: Viewport.OffsetX,
                    OffsetY: Viewport.OffsetY,
                    Zoom: Viewport.Zoom <= 0 ? 1 : Viewport.Zoom),
                Nodes: Nodes.Select(node => new GraphNode(
                    Id: node.Id,
                    Kind: node.Kind,
                    Position: new GraphPosition(node.Position.X, node.Position.Y),
                    Properties: node.Properties.ToImmutableDictionary(StringComparer.Ordinal),
                    Ports: node.Ports.Select(port => new GraphPort(
                        Id: port.Id,
                        Name: port.Name,
                        Direction: port.Direction)).ToImmutableArray()
                    )).ToImmutableArray(),
                Edges: Edges.Select(edge => new GraphEdge(
                    Id: edge.Id,
                    FromNodeId: edge.FromNodeId,
                    FromPortId: edge.FromPortId,
                    ToNodeId: edge.ToNodeId,
                    ToPortId: edge.ToPortId)).ToImmutableArray(),
                SelectedNodeIds: SelectedNodeIds.ToImmutableHashSet(StringComparer.Ordinal)
            );
        }
    }

    private sealed class GraphViewportDto
    {
        public double OffsetX { get; init; }

        public double OffsetY { get; init; }

        public double Zoom { get; init; } = 1;
    }

    private sealed class GraphPositionDto
    {
        public double X { get; init; }

        public double Y { get; init; }
    }

    private sealed class GraphNodeDto
    {
        public string Id { get; init; } = string.Empty;

        public string Kind { get; init; } = "processor";

        public GraphPositionDto Position { get; init; } = new();

        public Dictionary<string, string> Properties { get; init; } = new(StringComparer.Ordinal);

        public GraphPortDto[] Ports { get; init; } = [];
    }

    private sealed class GraphPortDto
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public GraphPortDirection Direction { get; init; }
    }

    private sealed class GraphEdgeDto
    {
        public string Id { get; init; } = string.Empty;

        public string FromNodeId { get; init; } = string.Empty;

        public string FromPortId { get; init; } = string.Empty;

        public string ToNodeId { get; init; } = string.Empty;

        public string ToPortId { get; init; } = string.Empty;
    }
}
