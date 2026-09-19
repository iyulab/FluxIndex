using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Services.KeywordSearch;
using FluxIndex.Storage.SQLite.KeywordSearch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxIndex.Storage.SQLite;

/// <summary>
/// Registration of the SQLite keyword (BM25) index for a container that is not built by
/// <c>FluxIndexContextBuilder</c> — the builder's <c>UseSQLite</c> registers it itself.
/// </summary>
public static class SQLiteKeywordSearchServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SQLiteKeywordSearchService"/> as the <see cref="IKeywordSearchService"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">
    /// The database to keep the index in. Null uses the database of the SQLite vector store registered in the
    /// same container (<c>AddSQLiteVecVectorStore</c> or <c>AddSQLiteVectorStore</c>), so both legs of a
    /// hybrid search live in one file.
    /// </param>
    /// <remarks>
    /// A registered <see cref="ITextAnalyzer"/> and <see cref="KeywordFieldOptions"/> are picked up from the
    /// container. Constructing the service by hand is where they get forgotten, and a keyword index built
    /// without them still answers — with the default tokenizer and no fields. Registered with
    /// <c>TryAdd</c>: a keyword service registered earlier is kept.
    /// </remarks>
    public static IServiceCollection AddSQLiteKeywordSearch(this IServiceCollection services, string? connectionString = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (connectionString is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton(sp => new SQLiteKeywordSearchService(
            connectionString ?? ResolveVectorStoreConnectionString(sp),
            sp.GetRequiredService<ILogger<SQLiteKeywordSearchService>>(),
            sp.GetService<ITextAnalyzer>(),
            sp.GetService<KeywordFieldOptions>()));
        services.TryAddSingleton<IKeywordSearchService>(sp => sp.GetRequiredService<SQLiteKeywordSearchService>());
        return services;
    }

    private static string ResolveVectorStoreConnectionString(IServiceProvider services)
    {
        // IOptions<T> always resolves, configured or not, so "was a store registered" is read from the
        // configure actions rather than from the options value.
        if (services.GetServices<IConfigureOptions<SQLiteVecOptions>>().Any())
            return services.GetRequiredService<IOptions<SQLiteVecOptions>>().Value.GetConnectionString();
        if (services.GetServices<IConfigureOptions<SQLiteOptions>>().Any())
            return services.GetRequiredService<IOptions<SQLiteOptions>>().Value.GetConnectionString();

        throw new InvalidOperationException(
            "AddSQLiteKeywordSearch was called without a connection string and no SQLite vector store is registered " +
            "to take the database from. Pass a connection string, or register AddSQLiteVecVectorStore/AddSQLiteVectorStore.");
    }
}
