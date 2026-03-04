using System.Text;
using System.Text.Json;
using NodeDesigner.Models.Graph;

namespace NodeDesigner.Services.Designer;

public sealed class NodeJsGeneratorService : INodeJsGeneratorService
{
    public string Generate(GraphDocument document)
    {
        var script = new StringBuilder();

        script.AppendLine("const nodes = new Map();");
        script.AppendLine("const links = [];");
        script.AppendLine();
        script.AppendLine("function createNode(config) {");
        script.AppendLine("  nodes.set(config.id, config);");
        script.AppendLine("  return config;");
        script.AppendLine("}");
        script.AppendLine();
        script.AppendLine("function connect(fromNodeId, fromPortId, toNodeId, toPortId) {");
        script.AppendLine("  links.push({ fromNodeId, fromPortId, toNodeId, toPortId });");
        script.AppendLine("}");
        script.AppendLine();

        if (document.Nodes.Length == 0)
        {
            script.AppendLine("// Graph is empty.");
        }

        foreach (var node in document.Nodes)
        {
            var serializedProperties = JsonSerializer.Serialize(node.Properties);

            script.AppendLine($"createNode({{");
            script.AppendLine($"  id: '{Escape(node.Id)}',");
            script.AppendLine($"  kind: '{Escape(node.Kind)}',");
            script.AppendLine($"  x: {node.Position.X:0.##},");
            script.AppendLine($"  y: {node.Position.Y:0.##},");
            script.AppendLine($"  props: {serializedProperties}");
            script.AppendLine("});");
            script.AppendLine();
        }

        foreach (var edge in document.Edges)
        {
            script.AppendLine($"connect('{Escape(edge.FromNodeId)}', '{Escape(edge.FromPortId)}', '{Escape(edge.ToNodeId)}', '{Escape(edge.ToPortId)}');");
        }

        script.AppendLine();
        script.AppendLine("module.exports = { nodes, links }; ");

        return script.ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
}
