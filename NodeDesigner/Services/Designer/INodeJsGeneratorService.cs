using NodeDesigner.Models.Graph;

namespace NodeDesigner.Services.Designer;

public interface INodeJsGeneratorService
{
    string Generate(GraphDocument document);
}
