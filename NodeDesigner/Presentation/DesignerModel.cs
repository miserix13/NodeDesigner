using System.ComponentModel;
using System.Runtime.CompilerServices;
using NodeDesigner.Models.Graph;
using NodeDesigner.Services.Designer;

namespace NodeDesigner.Presentation;

public sealed partial record DesignerModel : INotifyPropertyChanged
{
    private readonly INavigator _navigator;
    private readonly IGraphEditorService _graphEditorService;
    private readonly IGraphDocumentStore _graphDocumentStore;
    private readonly IUndoRedoService<GraphDocument> _undoRedoService;
    private readonly INodeJsGeneratorService _generatorService;
    private readonly IDesignerSurfaceHost _designerSurfaceHost;

    private GraphDocument _document;
    private string _generatedCode = string.Empty;
    private string? _activeDocumentPath;

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

        _document = _graphEditorService.CreateDocument();
        _generatedCode = _generatorService.Generate(_document);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title => "Visual Designer";

    public string RendererStatus => $"{_designerSurfaceHost.RendererName}: {_designerSurfaceHost.StatusMessage}";

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

    public bool CanUndo => _undoRedoService.CanUndo;

    public bool CanRedo => _undoRedoService.CanRedo;

    public async Task GoToMain()
    {
        await _navigator.NavigateViewModelAsync<MainModel>(this);
    }

    public Task AddNode()
    {
        ApplyChange(_graphEditorService.AddNode(_document, "processor"));
        return Task.CompletedTask;
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
        ApplyChange(_graphEditorService.ClearSelection(_document));
        return Task.CompletedTask;
    }

    public Task PanLeft()
    {
        ApplyChange(_graphEditorService.Pan(_document, -40, 0));
        return Task.CompletedTask;
    }

    public Task PanRight()
    {
        ApplyChange(_graphEditorService.Pan(_document, 40, 0));
        return Task.CompletedTask;
    }

    public Task PanUp()
    {
        ApplyChange(_graphEditorService.Pan(_document, 0, -40));
        return Task.CompletedTask;
    }

    public Task PanDown()
    {
        ApplyChange(_graphEditorService.Pan(_document, 0, 40));
        return Task.CompletedTask;
    }

    public Task ZoomIn()
    {
        ApplyChange(_graphEditorService.Zoom(_document, 1.1));
        return Task.CompletedTask;
    }

    public Task ZoomOut()
    {
        ApplyChange(_graphEditorService.Zoom(_document, 0.9));
        return Task.CompletedTask;
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
        _generatedCode = _generatorService.Generate(_document);

        OnPropertyChanged(nameof(ActiveDocumentPath));
        OnPropertyChanged(nameof(CanvasSummary));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(ViewportSummary));
        OnPropertyChanged(nameof(GeneratedCode));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (!string.IsNullOrWhiteSpace(propertyName))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
