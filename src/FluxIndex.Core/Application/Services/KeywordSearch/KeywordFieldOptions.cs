namespace FluxIndex.Core.Application.Services.KeywordSearch;

/// <summary>
/// A metadata-derived field the relational keyword index scores alongside the chunk body.
/// </summary>
/// <param name="MetadataKey">
/// The <see cref="Domain.Entities.DocumentChunk.Metadata"/> key whose value is analyzed and indexed
/// as this field. A chunk without the key simply has no text in the field.
/// </param>
/// <param name="Weight">
/// The BM25F weight of the field's term frequency. 1.0 counts a match in the field like a match in
/// the body of the same length; a short field such as a title already ranks well through length
/// normalization, so raise this only when a field match should outrank body matches outright.
/// </param>
public sealed record KeywordField(string MetadataKey, double Weight = 1.0);

/// <summary>
/// Which chunk metadata the relational keyword index (BM25) scores as fields, and how much each
/// field weighs (BM25F).
/// </summary>
/// <remarks>
/// <para>
/// The body of a chunk is always indexed. A field is a metadata value — a document title, the file
/// name — analyzed with the same <see cref="Interfaces.ITextAnalyzer"/> as the body and stored as
/// its own postings, so a query term that appears only in a title still retrieves the chunk. The
/// index and query paths share one instance of these options, so the two cannot disagree about
/// which fields exist.
/// </para>
/// <para>
/// The default scores <c>title</c> and <c>file_name</c> (the keys the FluxIndex SDK and FluxFeed
/// write) at weight 1.0. Register an instance in the container (<c>services.AddSingleton(new
/// KeywordFieldOptions { … })</c>) to change the set or the weights; <see cref="None"/> restores
/// body-only indexing. Changing the fields of an existing index changes what a query can match —
/// re-index the keyword leg after switching, exactly as for the analyzer.
/// </para>
/// </remarks>
public sealed class KeywordFieldOptions
{
    /// <summary>The name the body of a chunk is scored under; a field cannot use it.</summary>
    public const string BodyField = "content";

    /// <summary>Metadata key of the default title field.</summary>
    public const string TitleKey = "title";

    /// <summary>Metadata key of the default file-name field.</summary>
    public const string FileNameKey = "file_name";

    private readonly IReadOnlyList<KeywordField> _fields = [new(TitleKey), new(FileNameKey)];

    /// <summary>Body-only indexing: no metadata field is scored.</summary>
    public static KeywordFieldOptions None { get; } = new() { Fields = [] };

    /// <summary>
    /// The fields to index and score, in addition to the body. Keys must be distinct, non-empty and
    /// not <see cref="BodyField"/>; weights must be positive.
    /// </summary>
    public IReadOnlyList<KeywordField> Fields
    {
        get => _fields;
        init => _fields = Validate(value);
    }

    private static IReadOnlyList<KeywordField> Validate(IReadOnlyList<KeywordField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            ArgumentNullException.ThrowIfNull(field);
            if (string.IsNullOrWhiteSpace(field.MetadataKey))
                throw new ArgumentException("A keyword field needs a non-empty metadata key.", nameof(fields));
            if (string.Equals(field.MetadataKey, BodyField, StringComparison.Ordinal))
                throw new ArgumentException($"'{BodyField}' names the chunk body and cannot be a metadata field.", nameof(fields));
            if (!seen.Add(field.MetadataKey))
                throw new ArgumentException($"Keyword field '{field.MetadataKey}' is listed more than once.", nameof(fields));
            if (!(field.Weight > 0) || double.IsInfinity(field.Weight))
                throw new ArgumentException($"Keyword field '{field.MetadataKey}' needs a positive, finite weight.", nameof(fields));
        }

        return [.. fields];
    }
}
