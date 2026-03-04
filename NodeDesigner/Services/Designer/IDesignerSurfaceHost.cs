namespace NodeDesigner.Services.Designer;

public interface IDesignerSurfaceHost
{
    string RendererName { get; }

    bool IsSupported { get; }

    bool IsRunning { get; }

    string StatusMessage { get; }

    event EventHandler? StatusChanged;

    Task StartAsync(int requestedWidth, int requestedHeight, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    void ForwardPointerInput(
        double x,
        double y,
        bool leftButtonPressed,
        bool middleButtonPressed,
        bool rightButtonPressed,
        int wheelDelta = 0);
}
