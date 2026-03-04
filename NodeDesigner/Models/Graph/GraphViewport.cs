namespace NodeDesigner.Models.Graph;

public sealed record GraphViewport(double OffsetX, double OffsetY, double Zoom)
{
    public static GraphViewport Default { get; } = new(0, 0, 1);
}
