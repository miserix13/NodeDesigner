namespace NodeDesigner.Models.Graph;

public sealed partial record GraphEdge(
    string Id,
    string FromNodeId,
    string FromPortId,
    string ToNodeId,
    string ToPortId
);
