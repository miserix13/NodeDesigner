using System.Linq;
using NodeDesigner.Models.Graph;

namespace NodeDesigner.Services.Designer;

public sealed class GraphEditorService : IGraphEditorService
{
    private const double DefaultNodeSpacingX = 220;
    private const double DefaultNodeSpacingY = 140;

    public GraphDocument CreateDocument() => GraphDocument.Empty;

    public GraphDocument AddNode(GraphDocument document, string kind)
    {
        var index = document.Nodes.Length + 1;
        var position = new GraphPosition((index - 1) * DefaultNodeSpacingX, ((index - 1) % 5) * DefaultNodeSpacingY);

        return AddNodeAt(document, kind, position);
    }

    public GraphDocument AddNodeAt(GraphDocument document, string kind, GraphPosition position)
    {
        var index = document.Nodes.Length + 1;
        var nodeId = $"node_{index:D3}_{Guid.NewGuid().ToString("N")[..6]}";

        var ports = ImmutableArray.Create(
            new GraphPort($"{nodeId}_in", "In", GraphPortDirection.Input),
            new GraphPort($"{nodeId}_out", "Out", GraphPortDirection.Output)
        );

        var node = new GraphNode(
            Id: nodeId,
            Kind: kind,
            Position: position,
            Properties: ImmutableDictionary<string, string>.Empty.Add("name", $"Node {index}"),
            Ports: ports
        );

        return document with
        {
            Nodes = document.Nodes.Add(node),
            SelectedNodeIds = ImmutableHashSet.Create(nodeId),
        };
    }

    public GraphDocument ConnectLastTwoNodes(GraphDocument document)
    {
        if (document.Nodes.Length < 2)
        {
            return document;
        }

        return ConnectNodes(document, document.Nodes[^2].Id, document.Nodes[^1].Id);
    }

    public GraphDocument ConnectNodes(GraphDocument document, string fromNodeId, string toNodeId)
    {
        if (string.IsNullOrWhiteSpace(fromNodeId)
            || string.IsNullOrWhiteSpace(toNodeId)
            || string.Equals(fromNodeId, toNodeId, StringComparison.Ordinal))
        {
            return document;
        }

        var fromNode = document.Nodes.FirstOrDefault(node => string.Equals(node.Id, fromNodeId, StringComparison.Ordinal));
        var toNode = document.Nodes.FirstOrDefault(node => string.Equals(node.Id, toNodeId, StringComparison.Ordinal));

        if (fromNode is null || toNode is null)
        {
            return document;
        }

        var fromPort = fromNode.Ports.FirstOrDefault(port => port.Direction == GraphPortDirection.Output);
        var toPort = toNode.Ports.FirstOrDefault(port => port.Direction == GraphPortDirection.Input);

        if (fromPort is null || toPort is null)
        {
            return document;
        }

        var edgeExists = document.Edges.Any(edge =>
            edge.FromPortId == fromPort.Id
            && edge.ToPortId == toPort.Id
            && edge.FromNodeId == fromNode.Id
            && edge.ToNodeId == toNode.Id);

        if (edgeExists)
        {
            return document;
        }

        var edge = new GraphEdge(
            Id: $"edge_{Guid.NewGuid():N}",
            FromNodeId: fromNode.Id,
            FromPortId: fromPort.Id,
            ToNodeId: toNode.Id,
            ToPortId: toPort.Id
        );

        return document with
        {
            Edges = document.Edges.Add(edge),
        };
    }

    public GraphDocument RemoveSelectedNodes(GraphDocument document)
    {
        if (document.SelectedNodeIds.Count == 0)
        {
            return document;
        }

        var selectedNodeIds = document.SelectedNodeIds;

        var nodes = document.Nodes
            .Where(node => !selectedNodeIds.Contains(node.Id))
            .ToImmutableArray();

        var edges = document.Edges
            .Where(edge =>
                !selectedNodeIds.Contains(edge.FromNodeId)
                && !selectedNodeIds.Contains(edge.ToNodeId))
            .ToImmutableArray();

        return document with
        {
            Nodes = nodes,
            Edges = edges,
            SelectedNodeIds = ImmutableHashSet<string>.Empty,
        };
    }

    public GraphDocument MoveSelectedNodes(GraphDocument document, double deltaX, double deltaY)
    {
        if (document.SelectedNodeIds.Count == 0
            || (Math.Abs(deltaX) < double.Epsilon && Math.Abs(deltaY) < double.Epsilon))
        {
            return document;
        }

        var selectedNodeIds = document.SelectedNodeIds;

        var nodes = document.Nodes
            .Select(node =>
            {
                if (!selectedNodeIds.Contains(node.Id))
                {
                    return node;
                }

                return node with
                {
                    Position = new GraphPosition(node.Position.X + deltaX, node.Position.Y + deltaY),
                };
            })
            .ToImmutableArray();

        return document with
        {
            Nodes = nodes,
        };
    }

    public GraphDocument ToggleNodeSelection(GraphDocument document, string nodeId, bool multiSelect)
    {
        var selected = multiSelect ? document.SelectedNodeIds : ImmutableHashSet<string>.Empty;

        selected = selected.Contains(nodeId)
            ? selected.Remove(nodeId)
            : selected.Add(nodeId);

        return document with
        {
            SelectedNodeIds = selected,
        };
    }

    public GraphDocument SetNodeProperty(GraphDocument document, string nodeId, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(key))
        {
            return document;
        }

        var nodeIndex = -1;

        for (var index = 0; index < document.Nodes.Length; index++)
        {
            if (string.Equals(document.Nodes[index].Id, nodeId, StringComparison.Ordinal))
            {
                nodeIndex = index;
                break;
            }
        }

        if (nodeIndex < 0)
        {
            return document;
        }

        var node = document.Nodes[nodeIndex];
        var normalizedValue = value ?? string.Empty;
        var currentValue = node.Properties.TryGetValue(key, out var existing)
            ? existing
            : string.Empty;

        if (string.Equals(currentValue, normalizedValue, StringComparison.Ordinal))
        {
            return document;
        }

        var updatedNode = node with
        {
            Properties = node.Properties.SetItem(key, normalizedValue),
        };

        return document with
        {
            Nodes = document.Nodes.SetItem(nodeIndex, updatedNode),
        };
    }

    public GraphDocument ClearSelection(GraphDocument document)
    {
        if (document.SelectedNodeIds.Count == 0)
        {
            return document;
        }

        return document with
        {
            SelectedNodeIds = ImmutableHashSet<string>.Empty,
        };
    }

    public GraphDocument Pan(GraphDocument document, double deltaX, double deltaY)
    {
        return document with
        {
            Viewport = document.Viewport with
            {
                OffsetX = document.Viewport.OffsetX + deltaX,
                OffsetY = document.Viewport.OffsetY + deltaY,
            },
        };
    }

    public GraphDocument Zoom(GraphDocument document, double factor)
    {
        var updatedZoom = Math.Clamp(document.Viewport.Zoom * factor, 0.25, 3.0);

        return document with
        {
            Viewport = document.Viewport with
            {
                Zoom = updatedZoom,
            },
        };
    }
}
