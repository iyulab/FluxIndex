using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FluxIndex.Storage.PostgreSQL;

/// <summary>
/// A context whose model depends on the configured embedding dimension (the <c>vector(N)</c> column type).
/// </summary>
internal interface IEmbeddingDimensionsModel
{
    int EmbeddingDimensions { get; }
}

/// <summary>
/// Keys the EF Core model cache on the embedding dimension as well as the context type.
/// </summary>
/// <remarks>
/// EF Core builds a context's model once per context type and reuses it for every later instance. The vector column type
/// comes from <c>EmbeddingDimensions</c>, so without this key the first store configured in a process fixed the
/// dimension for all of them: a second store with a different dimension created its table as <c>vector(first)</c> and
/// every write to it failed with a dimension mismatch.
/// </remarks>
internal sealed class EmbeddingDimensionsModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
        => context is IEmbeddingDimensionsModel model
            ? (context.GetType(), model.EmbeddingDimensions, designTime)
            : (context.GetType(), designTime);
}
