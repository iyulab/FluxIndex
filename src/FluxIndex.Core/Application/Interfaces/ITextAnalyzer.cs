namespace FluxIndex.Core.Application.Interfaces;

/// <summary>
/// Turns text into the terms the keyword (BM25) index stores and queries.
/// </summary>
/// <remarks>
/// <para>
/// One analyzer owns tokenization, stop-word policy and minimum token length <em>together</em>:
/// the three are coupled, and swapping only the tokenizer while an English stop-word list and a
/// "two characters or more" rule stay in place silently drops single-syllable tokens in a language
/// that has them. The keyword index uses the same analyzer instance on the index path and on the
/// query path, so a consumer cannot end up with two sides that disagree.
/// </para>
/// <para>
/// Register one in the container (<c>services.AddSingleton&lt;ITextAnalyzer&gt;(…)</c>) and the
/// relational keyword backends pick it up; leave it out and <see cref="Services.KeywordSearch.DefaultTextAnalyzer"/>
/// applies. Changing the analyzer of an existing index changes what a query can match — re-index
/// after switching.
/// </para>
/// </remarks>
public interface ITextAnalyzer
{
    /// <summary>
    /// Produces the terms for <paramref name="text"/>, already normalized to the form the index
    /// stores (the default lower-cases). Returns nothing for null, empty or whitespace input.
    /// </summary>
    IEnumerable<string> Tokenize(string text);
}
