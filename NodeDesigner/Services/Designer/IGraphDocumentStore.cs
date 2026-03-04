using NodeDesigner.Models.Graph;

namespace NodeDesigner.Services.Designer;

public interface IGraphDocumentStore
{
    Task SaveAsync(string path, GraphDocument document, CancellationToken cancellationToken = default);

    Task<GraphDocument> LoadAsync(string path, CancellationToken cancellationToken = default);
}
