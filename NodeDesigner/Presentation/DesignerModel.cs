using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using Microsoft.UI.Dispatching;
using NodeDesigner.Models.Graph;
using NodeDesigner.Services.Designer;

namespace NodeDesigner.Presentation;

public sealed partial record DesignerModel : INotifyPropertyChanged
{
    private const double NodeWidth = 180;
    private const double NodeHeight = 96;

    private readonly INavigator _navigator;
    private readonly IGraphEditorService _graphEditorService;
    private readonly IGraphDocumentStore _graphDocumentStore;
    private readonly IUndoRedoService<GraphDocument> _undoRedoService;
    private readonly INodeJsGeneratorService _generatorService;
    private readonly IDesignerSurfaceHost _designerSurfaceHost;
    private readonly DispatcherQueue? _dispatcherQueue;

    private GraphDocument _document;
    private string _generatedCode = string.Empty;
    private string? _activeDocumentPath;
    private IReadOnlyList<DesignerNodeView> _nodes = [];
    private IReadOnlyList<DesignerEdgeView> _edges = [];

    public DesignerModel(
        INavigator navigator,
        IGraphEditorService graphEditorService,
        IGraphDocumentStore graphDocumentStore,
        IUndoRedoService<GraphDocument> undoRedoService,
        INodeJsGeneratorService generatorService,
        IDesignerSurfaceHost designerSurfaceHost)
    {
        _navigator = navigator;
        _graphEditorService = graphEditorService;
        _graphDocumentStore = graphDocumentStore;
        _undoRedoService = undoRedoService;
        _generatorService = generatorService;
        _designerSurfaceHost = designerSurfaceHost;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _designerSurfaceHost.StatusChanged += DesignerSurfaceHost_StatusChanged;

        _document = _graphEditorService.CreateDocument();
        RefreshState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title => "Visual Designer";

    public string RendererStatus =>
        $"{_designerSurfaceHost.RendererName} ({(_designerSurfaceHost.IsRunning ? "Running" : "Stopped")}): {_designerSurfaceHost.StatusMessage}";

    public string ActiveDocumentPath => string.IsNullOrWhiteSpace(_activeDocumentPath)
        ? "Unsaved graph"
        : _activeDocumentPath;

    public string CanvasSummary => $"{_document.Nodes.Length} nodes • {_document.Edges.Length} edges";

    public string SelectionSummary => _document.SelectedNodeIds.Count == 0
        ? "No nodes selected"
        : $"{_document.SelectedNodeIds.Count} nodes selected";

    public string ViewportSummary =>
        $"Pan ({_document.Viewport.OffsetX:0}, {_document.Viewport.OffsetY:0}) • Zoom {_document.Viewport.Zoom:0.00}x";

    public string GeneratedCode => _generatedCode;

    public IReadOnlyList<DesignerNodeView> Nodes => _nodes;

    public IReadOnlyList<DesignerEdgeView> Edges => _edges;

    public double ViewportOffsetX => _document.Viewport.OffsetX;

    public double ViewportOffsetY => _document.Viewport.OffsetY;

    public double ViewportZoom => _document.Viewport.Zoom;

    public bool HasSelection => _document.SelectedNodeIds.Count > 0;

    public string SelectedNodeKind => GetPrimarySelectedNode()?.Kind ?? "None";

    public string SelectedNodeName
    {
        get
        {
            var node = GetPrimarySelectedNode();

            if (node is null)
            {
                return string.Empty;
            }

            return node.Properties.TryGetValue("name", out var name)
                ? name
                : string.Empty;
        }
        set
        {
            var node = GetPrimarySelectedNode();

            if (node is null)
            {
                return;
            }

            ApplyChange(_graphEditorService.SetNodeProperty(_document, node.Id, "name", value ?? string.Empty));
        }
    }

    public bool CanUndo => _undoRedoService.CanUndo;

    public bool CanRedo => _undoRedoService.CanRedo;

    public async Task GoToMain()
    {
        await _navigator.NavigateViewModelAsync<MainModel>(this);
    }

    public Task StartRendererAsync(double surfaceWidth, double surfaceHeight)
    {
        var width = Math.Clamp(surfaceWidth <= 0 ? 1280 : (int)Math.Ceiling(surfaceWidth), 640, 4096);
        var height = Math.Clamp(surfaceHeight <= 0 ? 720 : (int)Math.Ceiling(surfaceHeight), 360, 4096);

        return _designerSurfaceHost.StartAsync(width, height);
    }

    public Task StopRendererAsync()
    {
        return _designerSurfaceHost.StopAsync();
    }

    public void ForwardPointerInput(
        double x,
        double y,
        bool leftButtonPressed,
        bool middleButtonPressed,
        bool rightButtonPressed,
        int wheelDelta = 0)
    {
        _designerSurfaceHost.ForwardPointerInput(
            x,
            y,
            leftButtonPressed,
            middleButtonPressed,
            rightButtonPressed,
            wheelDelta);
    }

    public Task AddNode()
    {
        ApplyChange(_graphEditorService.AddNode(_document, "processor"));
        return Task.CompletedTask;
    }

    public void AddNodeAt(double worldX, double worldY)
    {
        var position = new GraphPosition(worldX - (NodeWidth / 2), worldY - (NodeHeight / 2));
        ApplyChange(_graphEditorService.AddNodeAt(_document, "processor", position));
    }

    public Task SelectLastNode()
    {
        if (_document.Nodes.Length == 0)
        {
            return Task.CompletedTask;
        }

        var nodeId = _document.Nodes[^1].Id;
        ApplyChange(_graphEditorService.ToggleNodeSelection(_document, nodeId, multiSelect: false));
        return Task.CompletedTask;
    }

    public Task ConnectLastTwoNodes()
    {
        ApplyChange(_graphEditorService.ConnectLastTwoNodes(_document));
        return Task.CompletedTask;
    }

    public Task DeleteSelectedNodes()
    {
        ApplyChange(_graphEditorService.RemoveSelectedNodes(_document));
        return Task.CompletedTask;
    }

    public Task ClearSelection()
    {
        ClearSelectionImmediate();
        return Task.CompletedTask;
    }

    public void ClearSelectionImmediate()
    {
        ApplyChange(_graphEditorService.ClearSelection(_document));
    }

    public Task PanLeft()
    {
        PanBy(-40, 0);
        return Task.CompletedTask;
    }

    public Task PanRight()
    {
        PanBy(40, 0);
        return Task.CompletedTask;
    }

    public Task PanUp()
    {
        PanBy(0, -40);
        return Task.CompletedTask;
    }

    public Task PanDown()
    {
        PanBy(0, 40);
        return Task.CompletedTask;
    }

    public Task ZoomIn()
    {
        ZoomBy(1.1);
        return Task.CompletedTask;
    }

    public Task ZoomOut()
    {
        ZoomBy(0.9);
        return Task.CompletedTask;
    }

    public void PanBy(double deltaX, double deltaY)
    {
        ApplyChange(_graphEditorService.Pan(_document, deltaX, deltaY));
    }

    public void ZoomBy(double factor)
    {
        ApplyChange(_graphEditorService.Zoom(_document, factor));
    }

    public void MoveSelectedNodes(double deltaX, double deltaY)
    {
        ApplyChange(_graphEditorService.MoveSelectedNodes(_document, deltaX, deltaY));
    }

    public void SelectNode(string nodeId, bool multiSelect)
    {
        ApplyChange(_graphEditorService.ToggleNodeSelection(_document, nodeId, multiSelect));
    }

    public void ConnectPrimarySelectionTo(string targetNodeId)
    {
        var sourceNodeId = _document.SelectedNodeIds.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(sourceNodeId)
            || string.Equals(sourceNodeId, targetNodeId, StringComparison.Ordinal))
        {
            return;
        }

        ApplyChange(_graphEditorService.ConnectNodes(_document, sourceNodeId, targetNodeId));
    }

    public string? FindNodeAt(double worldX, double worldY)
    {
        for (var index = _nodes.Count - 1; index >= 0; index--)
        {
            var node = _nodes[index];

            if (worldX >= node.X
                && worldX <= node.X + node.Width
                && worldY >= node.Y
                && worldY <= node.Y + node.Height)
            {
                return node.Id;
            }
        }

        return null;
    }

    public Task Undo()
    {
        if (_undoRedoService.TryUndo(_document, out var previous))
        {
            _document = previous;
            RefreshState();
        }

        return Task.CompletedTask;
    }

    public Task Redo()
    {
        if (_undoRedoService.TryRedo(_document, out var next))
        {
            _document = next;
            RefreshState();
        }

        return Task.CompletedTask;
    }

    public async Task SaveGraph()
    {
        _activeDocumentPath ??= GetDefaultDocumentPath();
        await _graphDocumentStore.SaveAsync(_activeDocumentPath, _document);
        OnPropertyChanged(nameof(ActiveDocumentPath));
    }

    public async Task LoadGraph()
    {
        _activeDocumentPath ??= GetDefaultDocumentPath();
        _document = await _graphDocumentStore.LoadAsync(_activeDocumentPath);
        _undoRedoService.Clear();
        RefreshState();
    }

    private GraphNode? GetPrimarySelectedNode()
    {
        var selectedNodeId = _document.SelectedNodeIds.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(selectedNodeId))
        {
            return null;
        }

        return _document.Nodes.FirstOrDefault(node => string.Equals(node.Id, selectedNodeId, StringComparison.Ordinal));
    }

    private void RebuildGraphViews()
    {
        _nodes = _document.Nodes
            .Select(node =>
            {
                var name = node.Properties.TryGetValue("name", out var value) && !string.IsNullOrWhiteSpace(value)
                    ? value
                    : node.Id;

                return new DesignerNodeView
                {
                    Id = node.Id,
                    Name = name,
                    Kind = node.Kind,
                    X = node.Position.X,
                    Y = node.Position.Y,
                    Width = NodeWidth,
                    Height = NodeHeight,
                    IsSelected = _document.SelectedNodeIds.Contains(node.Id),
                };
            })
            .ToArray();

        var nodeLookup = _document.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var edges = new List<DesignerEdgeView>();

        foreach (var edge in _document.Edges)
        {
            if (!nodeLookup.TryGetValue(edge.FromNodeId, out var fromNode)
                || !nodeLookup.TryGetValue(edge.ToNodeId, out var toNode))
            {
                continue;
            }

            var fromPort = fromNode.Ports.FirstOrDefault(port => string.Equals(port.Id, edge.FromPortId, StringComparison.Ordinal));
            var toPort = toNode.Ports.FirstOrDefault(port => string.Equals(port.Id, edge.ToPortId, StringComparison.Ordinal));

            var fromX = fromNode.Position.X + NodeWidth;
            var toX = toNode.Position.X;

            if (fromPort?.Direction == GraphPortDirection.Input)
            {
                fromX = fromNode.Position.X;
            }

            if (toPort?.Direction == GraphPortDirection.Output)
            {
                toX = toNode.Position.X + NodeWidth;
            }

            edges.Add(new DesignerEdgeView
            {
                Id = edge.Id,
                X1 = fromX,
                Y1 = fromNode.Position.Y + (NodeHeight / 2),
                X2 = toX,
                Y2 = toNode.Position.Y + (NodeHeight / 2),
            });
        }

        _edges = edges;
    }

    private static string GetDefaultDocumentPath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NodeDesigner",
            "graphs");

        return Path.Combine(root, "designer.graph.json");
    }

    private void ApplyChange(GraphDocument updatedDocument)
    {
        if (updatedDocument == _document)
        {
            return;
        }

        _undoRedoService.Push(_document);
        _document = updatedDocument;
        RefreshState();
    }

    private void RefreshState()
    {
        RebuildGraphViews();
        _generatedCode = _generatorService.Generate(_document);

        OnPropertyChanged(nameof(ActiveDocumentPath));
        OnPropertyChanged(nameof(CanvasSummary));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(ViewportSummary));
        OnPropertyChanged(nameof(RendererStatus));
        OnPropertyChanged(nameof(Nodes));
        OnPropertyChanged(nameof(Edges));
        OnPropertyChanged(nameof(ViewportOffsetX));
        OnPropertyChanged(nameof(ViewportOffsetY));
        OnPropertyChanged(nameof(ViewportZoom));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedNodeKind));
        OnPropertyChanged(nameof(SelectedNodeName));
        OnPropertyChanged(nameof(GeneratedCode));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void DesignerSurfaceHost_StatusChanged(object? sender, EventArgs e)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            OnPropertyChanged(nameof(RendererStatus));
            return;
        }

        _dispatcherQueue.TryEnqueue(() => OnPropertyChanged(nameof(RendererStatus)));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (!string.IsNullOrWhiteSpace(propertyName))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
