namespace NodeDesigner.Models.Graph;

public sealed partial record GraphNode(
    string Id,
    string Kind,
    GraphPosition Position,
    ImmutableDictionary<string, string> Properties,
    ImmutableArray<GraphPort> Ports
);
