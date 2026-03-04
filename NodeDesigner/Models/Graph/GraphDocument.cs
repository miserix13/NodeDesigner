namespace NodeDesigner.Models.Graph;

public sealed record GraphDocument(
    int Version,
    GraphViewport Viewport,
    ImmutableArray<GraphNode> Nodes,
    ImmutableArray<GraphEdge> Edges,
    ImmutableHashSet<string> SelectedNodeIds
)
{
    public static GraphDocument Empty { get; } = new(
        Version: 1,
        Viewport: GraphViewport.Default,
        Nodes: ImmutableArray<GraphNode>.Empty,
        Edges: ImmutableArray<GraphEdge>.Empty,
        SelectedNodeIds: ImmutableHashSet<string>.Empty
    );
}
