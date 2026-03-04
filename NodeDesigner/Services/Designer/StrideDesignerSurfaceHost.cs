using System.Linq;
using NodeDesigner.Models.Graph;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Lights;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
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
        private readonly Dictionary<string, List<StrideEntity>> _edgeSegmentEntities = new(StringComparer.Ordinal);

        private Scene? _scene;
        private StrideEntity? _cameraEntity;
        private CameraComponent? _cameraComponent;
        private Model? _nodeModel;
        private Model? _selectedNodeModel;
        private Model? _edgeModel;
        private float _cameraDistance = 24f;
        private Vector2 _cameraCenter = Vector2.Zero;
        private int _lastAppliedRevision = -1;

        protected override async Task LoadContent()
        {
            await base.LoadContent();

            IsMouseVisible = true;

            _scene = new Scene();
            SceneSystem.SceneInstance = new SceneInstance(Services, _scene);

            CreateCameraAndLights(_scene);

            _nodeModel = CreateCubeModel(Services, new Color4(0.24f, 0.56f, 0.92f, 1f), emissiveIntensity: 0.08f);
            _selectedNodeModel = CreateCubeModel(Services, new Color4(0.99f, 0.72f, 0.28f, 1f), emissiveIntensity: 0.22f);
            _edgeModel = CreateCubeModel(Services, new Color4(0.74f, 0.78f, 0.88f, 1f), emissiveIntensity: 0f);
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
            if (_scene is null || _nodeModel is null || _selectedNodeModel is null || _edgeModel is null)
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

                var nodeModelComponent = nodeEntity.Get<ModelComponent>();

                if (nodeModelComponent is not null)
                {
                    nodeModelComponent.Model = isSelected ? _selectedNodeModel : _nodeModel;
                }

                nodeEntity.Transform.Position = new Vector3(worldPosition.X, worldPosition.Y, isSelected ? 0.24f : 0.04f);
                nodeEntity.Transform.Rotation = Quaternion.Identity;
                nodeEntity.Transform.Scale = isSelected
                    ? new Vector3(1.96f, 1.06f, 0.6f)
                    : new Vector3(1.8f, 1.0f, 0.4f);

                nodeWorldPositions[node.Id] = worldPosition;
            }

            RemoveStaleEntities(_scene, _nodeEntities, activeNodeIds);
            UpdateCameraFrame(nodeWorldPositions);

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

                var edgePoints = BuildEdgeCurvePoints(start, end, edge.Id);

                if (edgePoints.Count < 2)
                {
                    continue;
                }

                if (!_edgeSegmentEntities.TryGetValue(edge.Id, out var edgeSegments))
                {
                    edgeSegments = [];
                    _edgeSegmentEntities[edge.Id] = edgeSegments;
                }

                var segmentCount = edgePoints.Count - 1;
                EnsureEdgeSegmentCount(_scene, edgeSegments, segmentCount, edge.Id, _edgeModel);

                var edgeThickness = ComputeEdgeThickness(edgePoints[0], edgePoints[^1]);

                for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
                {
                    var segmentStart = edgePoints[segmentIndex];
                    var segmentEnd = edgePoints[segmentIndex + 1];
                    var segmentVector = segmentEnd - segmentStart;
                    var segmentLength = segmentVector.Length();

                    if (segmentLength <= float.Epsilon)
                    {
                        continue;
                    }

                    var segmentDirection = segmentVector / segmentLength;
                    var segmentEntity = edgeSegments[segmentIndex];

                    segmentEntity.Transform.Position = new Vector3(
                        (segmentStart.X + segmentEnd.X) * 0.5f,
                        (segmentStart.Y + segmentEnd.Y) * 0.5f,
                        -0.22f);
                    segmentEntity.Transform.Rotation = Quaternion.BetweenDirections(Vector3.UnitX, segmentDirection);
                    segmentEntity.Transform.Scale = new Vector3(segmentLength, edgeThickness, edgeThickness);
                }
            }

            RemoveStaleEdgeSegments(_scene, _edgeSegmentEntities, activeEdgeIds);
        }

        private void CreateCameraAndLights(Scene scene)
        {
            _cameraComponent = new CameraComponent();
            _cameraEntity = new StrideEntity("preview-camera")
            {
                _cameraComponent,
            };

            _cameraEntity.Transform.Position = new Vector3(0f, 0f, -24f);
            scene.Entities.Add(_cameraEntity);

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

        private static Model CreateCubeModel(IServiceRegistry services, Color4 color, float emissiveIntensity)
        {
            var model = new Model();
            var proceduralModel = new CubeProceduralModel
            {
                Size = Vector3.One,
            };

            proceduralModel.Generate(services, model);
            model.Materials.Clear();
            model.Materials.Add(new MaterialInstance(CreateLitMaterial(services, color, emissiveIntensity)));

            return model;
        }

        private static Material CreateLitMaterial(IServiceRegistry services, Color4 color, float emissiveIntensity)
        {
            var graphicsDevice = services.GetSafeServiceAs<GraphicsDevice>();

            var descriptor = new MaterialDescriptor
            {
                Attributes = new MaterialAttributes
                {
                    DiffuseModel = new MaterialDiffuseLambertModelFeature(),
                    Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(color)),
                },
            };

            if (emissiveIntensity > 0f)
            {
                descriptor.Attributes.Emissive = new MaterialEmissiveMapFeature(new ComputeColor(color))
                {
                    Intensity = new ComputeFloat(emissiveIntensity),
                };
            }

            return Material.New(graphicsDevice, descriptor);
        }

        private static List<Vector3> BuildEdgeCurvePoints(Vector3 start, Vector3 end, string edgeId)
        {
            const int segmentCount = 8;

            var points = new List<Vector3>(segmentCount + 1);
            var direction = end.X >= start.X ? 1f : -1f;
            var horizontalDistance = MathF.Abs(end.X - start.X);
            var bendDistance = Math.Clamp((horizontalDistance * 0.35f) + 0.55f, 0.85f, 3.2f);
            var laneOffset = ((Math.Abs(edgeId.GetHashCode()) % 5) - 2) * 0.22f;

            var controlPoint1 = start + new Vector3(direction * bendDistance, laneOffset, 0f);
            var controlPoint2 = end - new Vector3(direction * bendDistance, -laneOffset, 0f);

            for (var index = 0; index <= segmentCount; index++)
            {
                var t = index / (float)segmentCount;
                points.Add(EvaluateCubicBezier(start, controlPoint1, controlPoint2, end, t));
            }

            return points;
        }

        private static Vector3 EvaluateCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var u = 1f - t;
            var tt = t * t;
            var uu = u * u;
            var uuu = uu * u;
            var ttt = tt * t;

            return (p0 * uuu)
                + (p1 * (3f * uu * t))
                + (p2 * (3f * u * tt))
                + (p3 * ttt);
        }

        private static float ComputeEdgeThickness(Vector3 start, Vector3 end)
        {
            var chordLength = (end - start).Length();
            return Math.Clamp(0.085f + (MathF.Min(chordLength, 16f) * 0.0025f), 0.085f, 0.125f);
        }

        private static void EnsureEdgeSegmentCount(
            Scene scene,
            List<StrideEntity> edgeSegments,
            int requiredSegments,
            string edgeId,
            Model edgeModel)
        {
            while (edgeSegments.Count < requiredSegments)
            {
                var segmentEntity = new StrideEntity($"edge:{edgeId}:segment:{edgeSegments.Count}")
                {
                    new ModelComponent
                    {
                        Model = edgeModel,
                    },
                };

                scene.Entities.Add(segmentEntity);
                edgeSegments.Add(segmentEntity);
            }

            while (edgeSegments.Count > requiredSegments)
            {
                var removeIndex = edgeSegments.Count - 1;
                var segmentEntity = edgeSegments[removeIndex];
                scene.Entities.Remove(segmentEntity);
                edgeSegments.RemoveAt(removeIndex);
            }
        }

        private static Vector3 ToWorldPosition(GraphPosition position, double centerX, double centerY)
        {
            return new Vector3(
                (float)((position.X - centerX) / WorldUnitsPerGraphUnit),
                (float)((centerY - position.Y) / WorldUnitsPerGraphUnit),
                0f);
        }

        private void UpdateCameraFrame(IReadOnlyDictionary<string, Vector3> nodeWorldPositions)
        {
            if (_cameraEntity is null || _cameraComponent is null)
            {
                return;
            }

            float minX;
            float maxX;
            float minY;
            float maxY;

            if (nodeWorldPositions.Count == 0)
            {
                minX = -3f;
                maxX = 3f;
                minY = -2f;
                maxY = 2f;
            }
            else
            {
                minX = float.MaxValue;
                maxX = float.MinValue;
                minY = float.MaxValue;
                maxY = float.MinValue;

                foreach (var position in nodeWorldPositions.Values)
                {
                    minX = MathF.Min(minX, position.X - 1.25f);
                    maxX = MathF.Max(maxX, position.X + 1.25f);
                    minY = MathF.Min(minY, position.Y - 0.85f);
                    maxY = MathF.Max(maxY, position.Y + 0.85f);
                }
            }

            const float margin = 0.7f;
            minX -= margin;
            maxX += margin;
            minY -= margin;
            maxY += margin;

            var halfWidth = MathF.Max(2f, (maxX - minX) * 0.5f);
            var halfHeight = MathF.Max(1.5f, (maxY - minY) * 0.5f);
            var centerX = (minX + maxX) * 0.5f;
            var centerY = (minY + maxY) * 0.5f;

            var aspectRatio = _cameraComponent.ActuallyUsedAspectRatio;

            if (aspectRatio <= 0.01f)
            {
                aspectRatio = 16f / 9f;
            }

            var requiredHalfHeight = MathF.Max(halfHeight, halfWidth / aspectRatio);
            var verticalFieldOfView = _cameraComponent.VerticalFieldOfView;

            if (verticalFieldOfView <= 0.01f)
            {
                verticalFieldOfView = MathUtil.PiOverFour;
            }

            var targetDistance = (requiredHalfHeight / MathF.Tan(verticalFieldOfView * 0.5f)) + 4.5f;
            targetDistance = Math.Clamp(targetDistance, 9f, 180f);

            _cameraDistance = MathUtil.Lerp(_cameraDistance, targetDistance, 0.2f);
            _cameraCenter = new Vector2(
                MathUtil.Lerp(_cameraCenter.X, centerX, 0.2f),
                MathUtil.Lerp(_cameraCenter.Y, centerY, 0.2f));

            _cameraEntity.Transform.Position = new Vector3(_cameraCenter.X, _cameraCenter.Y, -_cameraDistance);
            _cameraEntity.Transform.Rotation = Quaternion.Identity;
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

        private static void RemoveStaleEdgeSegments(
            Scene scene,
            Dictionary<string, List<StrideEntity>> edgeLookup,
            HashSet<string> activeIds)
        {
            var staleIds = edgeLookup.Keys
                .Where(id => !activeIds.Contains(id))
                .ToArray();

            foreach (var staleId in staleIds)
            {
                var segmentEntities = edgeLookup[staleId];

                foreach (var segmentEntity in segmentEntities)
                {
                    scene.Entities.Remove(segmentEntity);
                }

                edgeLookup.Remove(staleId);
            }
        }
    }
}
