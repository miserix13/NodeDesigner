namespace NodeDesigner.Presentation;

public sealed class DesignerNodeView
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public bool IsSelected { get; init; }

    public double Opacity => IsSelected ? 1.0 : 0.88;
}
