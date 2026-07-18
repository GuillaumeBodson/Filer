namespace Filer.Modules.Search.Contracts;

/// <summary>
/// Stable machine-readable error codes surfaced by the Search module in
/// problem-details responses (03-api-specification.md). The values deliberately
/// coincide with the Documents list contract's paging/search codes so clients
/// branch on one vocabulary; each module still owns the codes its endpoints
/// surface (10-solution-structure.md).
/// </summary>
public static class SearchErrorCodes
{
    /// <summary>The <c>?q=</c> term is missing, blank, or exceeds the accepted length — 400.</summary>
    public const string SearchTermInvalid = "search_term_invalid";

    /// <summary>The <c>?page=</c> parameter is below 1 — 400.</summary>
    public const string PageInvalid = "page_invalid";

    /// <summary>The <c>?pageSize=</c> parameter is outside the accepted range — 400.</summary>
    public const string PageSizeInvalid = "page_size_invalid";
}
