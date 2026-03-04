using NodeDesigner.Models.Graph;

namespace NodeDesigner.Services.Designer;

public interface IGraphEditorService
{
    GraphDocument CreateDocument();

    GraphDocument AddNode(GraphDocument document, string kind);

    GraphDocument ConnectLastTwoNodes(GraphDocument document);

    GraphDocument RemoveSelectedNodes(GraphDocument document);

    GraphDocument ToggleNodeSelection(GraphDocument document, string nodeId, bool multiSelect);

    GraphDocument ClearSelection(GraphDocument document);

    GraphDocument Pan(GraphDocument document, double deltaX, double deltaY);

    GraphDocument Zoom(GraphDocument document, double factor);
}
