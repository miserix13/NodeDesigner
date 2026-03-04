using Stride.Engine;
using Stride.Games;

namespace NodeDesigner.Services.Designer;

public sealed class StrideDesignerSurfaceHost : IDesignerSurfaceHost
{
    private readonly object _gate = new();

    private Task? _runTask;
    private Game? _game;
    private string _status = "Stride preview host is ready.";
    private string _lastInput = "No forwarded input yet.";
    private int _pointerEvents;
    private int _wheelEvents;
    private bool _isRunning;

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
                return $"{_status} {_lastInput} Forwarded input: {_pointerEvents} pointer / {_wheelEvents} wheel.";
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
        Game? game;
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

    private void RunGameLoop(int width, int height)
    {
        try
        {
            using var game = new Game();

            lock (_gate)
            {
                _game = game;
                _isRunning = true;
                _status = $"Stride preview running in external SDL window ({width}x{height}).";
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

    private void OnStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);
}
