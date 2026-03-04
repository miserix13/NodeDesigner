using System.Linq;
using NodeDesigner.Models.Graph;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Rendering;
using Stride.Rendering.Lights;
using Stride.Rendering.ProceduralModels;
using StrideEntity = Stride.Engine.Entity;

namespace NodeDesigner.Services.Designer;

public sealed class StrideDesignerSurfaceHost : IDesignerSurfaceHost
{
    private const float WorldUnitsPerGraphUnit = 42f;
    private const float NodeHalfWidth = 0.9f;

    private readonly object _gate = new();

    private Task? _runTask;
    private DesignerPreviewGame? _game;
    private string _status = "Stride preview host is ready.";
    private string _lastInput = "No forwarded input yet.";
    private int _pointerEvents;
    private int _wheelEvents;
    private bool _isRunning;
    private GraphDocument _latestGraphDocument = GraphDocument.Empty;
    private int _latestGraphRevision;
    private int _latestNodeCount;
    private int _latestEdgeCount;

    public string RendererName => "Stride";

    public bool IsSupported => true;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _isRunning;
            }
        }
    }

    public string StatusMessage
    {
        get
        {
            lock (_gate)
            {
                return $"{_status} Graph: {_latestNodeCount} nodes / {_latestEdgeCount} edges. {_lastInput} Forwarded input: {_pointerEvents} pointer / {_wheelEvents} wheel.";
            }
        }
    }

    public event EventHandler? StatusChanged;

    public Task StartAsync(int requestedWidth, int requestedHeight, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        lock (_gate)
        {
            if (_runTask is { IsCompleted: false })
            {
                _status = "Stride preview is already running.";
            }
            else
            {
                var width = Math.Clamp(requestedWidth <= 0 ? 1280 : requestedWidth, 640, 4096);
                var height = Math.Clamp(requestedHeight <= 0 ? 720 : requestedHeight, 360, 4096);

                _pointerEvents = 0;
                _wheelEvents = 0;
                _lastInput = "No forwarded input yet.";
                _status = $"Starting Stride preview ({width}x{height})...";

                _runTask = Task.Factory.StartNew(
                    () => RunGameLoop(width, height),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
        }

        OnStatusChanged();

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? runTask;
        DesignerPreviewGame? game;
        var shouldRaiseNotRunning = false;

        lock (_gate)
        {
            runTask = _runTask;
            game = _game;

            if (runTask is null || runTask.IsCompleted)
            {
                _status = "Stride preview is not running.";
                shouldRaiseNotRunning = true;
            }
            else
            {
                _status = "Stopping Stride preview...";
            }
        }

        if (shouldRaiseNotRunning)
        {
            OnStatusChanged();
            return;
        }

        OnStatusChanged();

        try
        {
            game?.Exit();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Stride preview stop signal failed: {ex.GetBaseException().Message}");
        }

        if (runTask is null)
        {
            return;
        }

        try
        {
            await runTask.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        catch (TimeoutException)
        {
            UpdateStatus("Stride preview is still shutting down.");
        }
    }

    public void ForwardPointerInput(
        double x,
        double y,
        bool leftButtonPressed,
        bool middleButtonPressed,
        bool rightButtonPressed,
        int wheelDelta = 0)
    {
        var shouldRaise = false;

        lock (_gate)
        {
            _pointerEvents++;

            if (wheelDelta != 0)
            {
                _wheelEvents++;
            }

            if (_pointerEvents == 1 || wheelDelta != 0 || _pointerEvents % 30 == 0)
            {
                _lastInput = $"Last input ({x:0.0}, {y:0.0}) L:{leftButtonPressed} M:{middleButtonPressed} R:{rightButtonPressed} Δ:{wheelDelta}.";
                shouldRaise = true;
            }
        }

        if (shouldRaise)
        {
            OnStatusChanged();
        }
    }

    public void UpdateGraphPreview(GraphDocument document)
    {
        lock (_gate)
        {
            _latestGraphDocument = document ?? GraphDocument.Empty;
            _latestNodeCount = _latestGraphDocument.Nodes.Length;
            _latestEdgeCount = _latestGraphDocument.Edges.Length;
            _latestGraphRevision++;
        }

        if (IsRunning)
        {
            OnStatusChanged();
        }
    }

    private void RunGameLoop(int width, int height)
    {
        try
        {
            using var game = new DesignerPreviewGame(GetGraphSnapshot);

            lock (_gate)
            {
                _game = game;
                _isRunning = true;
                _status = $"Stride preview running in external SDL window ({width}x{height}) with live graph sync.";
            }

            OnStatusChanged();

            var context = GameContextFactory.NewGameContext(AppContextType.DesktopSDL, width, height, false);

            game.Run(context);

            UpdateStatus("Stride preview window exited.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Stride preview failed: {ex.GetBaseException().Message}");
        }
        finally
        {
            lock (_gate)
            {
                _isRunning = false;
                _game = null;
                _runTask = null;
            }

            OnStatusChanged();
        }
    }

    private void UpdateStatus(string message)
    {
        lock (_gate)
        {
            _status = message;
        }

        OnStatusChanged();
    }

    private (GraphDocument Document, int Revision) GetGraphSnapshot()
    {
        lock (_gate)
        {
            return (_latestGraphDocument, _latestGraphRevision);
        }
    }

    private void OnStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    private sealed class DesignerPreviewGame(Func<(GraphDocument Document, int Revision)> snapshotProvider) : Game
    {
        private readonly Func<(GraphDocument Document, int Revision)> _snapshotProvider = snapshotProvider;
        private readonly Dictionary<string, StrideEntity> _nodeEntities = new(StringComparer.Ordinal);
        private readonly Dictionary<string, StrideEntity> _edgeEntities = new(StringComparer.Ordinal);

        private Scene? _scene;
        private Model? _nodeModel;
        private Model? _edgeModel;
        private int _lastAppliedRevision = -1;

        protected override async Task LoadContent()
        {
            await base.LoadContent();

            IsMouseVisible = true;

            _scene = new Scene();
            SceneSystem.SceneInstance = new SceneInstance(Services, _scene);

            CreateCameraAndLights(_scene);

            _nodeModel = CreateCubeModel(Services);
            _edgeModel = CreateCubeModel(Services);
        }

        protected override void Update(GameTime gameTime)
        {
            var snapshot = _snapshotProvider();

            if (snapshot.Revision != _lastAppliedRevision)
            {
                ApplyGraphSnapshot(snapshot.Document);
                _lastAppliedRevision = snapshot.Revision;
            }

            base.Update(gameTime);
        }

        private void ApplyGraphSnapshot(GraphDocument document)
        {
            if (_scene is null || _nodeModel is null || _edgeModel is null)
            {
                return;
            }

            var nodes = document.Nodes;
            var selectedIds = document.SelectedNodeIds;

            var centerX = nodes.Length == 0 ? 0.0 : nodes.Average(node => node.Position.X);
            var centerY = nodes.Length == 0 ? 0.0 : nodes.Average(node => node.Position.Y);

            var nodeWorldPositions = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            var activeNodeIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in nodes)
            {
                activeNodeIds.Add(node.Id);

                if (!_nodeEntities.TryGetValue(node.Id, out var nodeEntity))
                {
                    nodeEntity = new StrideEntity($"node:{node.Id}")
                    {
                        new ModelComponent
                        {
                            Model = _nodeModel,
                        },
                    };

                    _scene.Entities.Add(nodeEntity);
                    _nodeEntities[node.Id] = nodeEntity;
                }

                var worldPosition = ToWorldPosition(node.Position, centerX, centerY);
                var isSelected = selectedIds.Contains(node.Id);

                nodeEntity.Transform.Position = new Vector3(worldPosition.X, worldPosition.Y, isSelected ? 0.2f : 0f);
                nodeEntity.Transform.Rotation = Quaternion.Identity;
                nodeEntity.Transform.Scale = isSelected
                    ? new Vector3(1.9f, 1.0f, 0.55f)
                    : new Vector3(1.8f, 1.0f, 0.4f);

                nodeWorldPositions[node.Id] = worldPosition;
            }

            RemoveStaleEntities(_scene, _nodeEntities, activeNodeIds);

            var activeEdgeIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var edge in document.Edges)
            {
                if (!nodeWorldPositions.TryGetValue(edge.FromNodeId, out var fromPosition)
                    || !nodeWorldPositions.TryGetValue(edge.ToNodeId, out var toPosition))
                {
                    continue;
                }

                var fromNode = nodes.FirstOrDefault(node => string.Equals(node.Id, edge.FromNodeId, StringComparison.Ordinal));
                var toNode = nodes.FirstOrDefault(node => string.Equals(node.Id, edge.ToNodeId, StringComparison.Ordinal));

                if (fromNode is null || toNode is null)
                {
                    continue;
                }

                var fromPort = fromNode.Ports.FirstOrDefault(port => string.Equals(port.Id, edge.FromPortId, StringComparison.Ordinal));
                var toPort = toNode.Ports.FirstOrDefault(port => string.Equals(port.Id, edge.ToPortId, StringComparison.Ordinal));

                var start = fromPosition;
                var end = toPosition;

                start.X += fromPort?.Direction == GraphPortDirection.Input ? -NodeHalfWidth : NodeHalfWidth;
                end.X += toPort?.Direction == GraphPortDirection.Output ? NodeHalfWidth : -NodeHalfWidth;

                var edgeVector = end - start;
                var edgeLength = edgeVector.Length();

                if (edgeLength <= float.Epsilon)
                {
                    continue;
                }

                activeEdgeIds.Add(edge.Id);

                if (!_edgeEntities.TryGetValue(edge.Id, out var edgeEntity))
                {
                    edgeEntity = new StrideEntity($"edge:{edge.Id}")
                    {
                        new ModelComponent
                        {
                            Model = _edgeModel,
                        },
                    };

                    _scene.Entities.Add(edgeEntity);
                    _edgeEntities[edge.Id] = edgeEntity;
                }

                var edgeDirection = edgeVector / edgeLength;

                edgeEntity.Transform.Position = (start + end) * 0.5f;
                edgeEntity.Transform.Rotation = Quaternion.BetweenDirections(Vector3.UnitX, edgeDirection);
                edgeEntity.Transform.Scale = new Vector3(edgeLength, 0.1f, 0.1f);
            }

            RemoveStaleEntities(_scene, _edgeEntities, activeEdgeIds);
        }

        private static void CreateCameraAndLights(Scene scene)
        {
            var cameraEntity = new StrideEntity("preview-camera")
            {
                new CameraComponent(),
            };

            cameraEntity.Transform.Position = new Vector3(0f, 0f, -24f);
            scene.Entities.Add(cameraEntity);

            var ambientLight = new StrideEntity("preview-ambient")
            {
                new LightComponent
                {
                    Type = new LightAmbient(),
                },
            };

            scene.Entities.Add(ambientLight);

            var directionalLight = new StrideEntity("preview-directional")
            {
                new LightComponent
                {
                    Type = new LightDirectional(),
                },
            };

            directionalLight.Transform.Rotation = Quaternion.RotationYawPitchRoll(0.55f, -0.8f, 0f);
            scene.Entities.Add(directionalLight);
        }

        private static Model CreateCubeModel(IServiceRegistry services)
        {
            var model = new Model();
            var proceduralModel = new CubeProceduralModel
            {
                Size = Vector3.One,
            };

            proceduralModel.Generate(services, model);

            return model;
        }

        private static Vector3 ToWorldPosition(GraphPosition position, double centerX, double centerY)
        {
            return new Vector3(
                (float)((position.X - centerX) / WorldUnitsPerGraphUnit),
                (float)((centerY - position.Y) / WorldUnitsPerGraphUnit),
                0f);
        }

        private static void RemoveStaleEntities(
            Scene scene,
            Dictionary<string, StrideEntity> entityLookup,
            HashSet<string> activeIds)
        {
            var staleIds = entityLookup.Keys
                .Where(id => !activeIds.Contains(id))
                .ToArray();

            foreach (var staleId in staleIds)
            {
                var staleEntity = entityLookup[staleId];
                scene.Entities.Remove(staleEntity);
                entityLookup.Remove(staleId);
            }
        }
    }
}
