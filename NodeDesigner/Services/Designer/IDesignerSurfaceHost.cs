namespace NodeDesigner.Services.Designer;

public interface IDesignerSurfaceHost
{
    string RendererName { get; }

    bool IsSupported { get; }

    string StatusMessage { get; }
}
