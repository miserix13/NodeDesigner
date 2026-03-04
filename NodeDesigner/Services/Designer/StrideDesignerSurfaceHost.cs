namespace NodeDesigner.Services.Designer;

public sealed class StrideDesignerSurfaceHost : IDesignerSurfaceHost
{
    public string RendererName => "Stride";

    public bool IsSupported => false;

    public string StatusMessage => "Stride host integration scaffold is active for the designer route.";
}
