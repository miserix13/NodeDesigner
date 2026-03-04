using NodeDesigner.Models.Graph;

namespace NodeDesigner.Services.Designer;

public interface IGraphEditorService
{
    GraphDocument CreateDocument();

    GraphDocument AddNode(GraphDocument document, string kind);

    GraphDocument AddNodeAt(GraphDocument document, string kind, GraphPosition position);

    GraphDocument ConnectLastTwoNodes(GraphDocument document);

    GraphDocument ConnectNodes(GraphDocument document, string fromNodeId, string toNodeId);

    GraphDocument RemoveSelectedNodes(GraphDocument document);

    GraphDocument MoveSelectedNodes(GraphDocument document, double deltaX, double deltaY);

    GraphDocument ToggleNodeSelection(GraphDocument document, string nodeId, bool multiSelect);

    GraphDocument SetNodeProperty(GraphDocument document, string nodeId, string key, string value);

    GraphDocument ClearSelection(GraphDocument document);

    GraphDocument Pan(GraphDocument document, double deltaX, double deltaY);

    GraphDocument Zoom(GraphDocument document, double factor);
}
