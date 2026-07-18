using System.Text;
using System.Text.RegularExpressions;

namespace Filer.Modules.Documents.Persistence;

/// <summary>
/// Turns a raw user search term into the tsquery strategy used by
/// <see cref="EfOwnerDocumentSearch"/> (#57). Two modes:
/// <list type="bullet">
/// <item><description>
/// Plain words (the common case) become an explicit AND-of-lexemes
/// <c>to_tsquery</c> string whose <b>last</b> token matches by prefix
/// (<c>fact & 2024:*</c>) — the search-as-you-type behavior a file search
/// needs, and the compensation for the stemming-free 'simple' regconfig
/// ("fact" finds "facture").
/// </description></item>
/// <item><description>
/// Terms using websearch operators (quoted phrases, OR, leading-dash
/// exclusion) are handed to <c>websearch_to_tsquery</c> unchanged, which is
/// safe on arbitrary input; prefix matching is deliberately not combined with
/// operator syntax.
/// </description></item>
/// </list>
/// </summary>
internal static partial class SearchTermTsQuery
{
    /// <summary>
    /// A quoted phrase, an <c>or</c> between words, or a word-leading dash —
    /// the three operators websearch_to_tsquery gives meaning to (case-insensitive
    /// <c>or</c>, matching Postgres).
    /// </summary>
    [GeneratedRegex(@"""|(^|\s)-\S|(?i:(^|\s)or(\s|$))")]
    private static partial Regex WebsearchOperators { get; }

    /// <summary>Whether the term should be parsed by <c>websearch_to_tsquery</c>.</summary>
    internal static bool UsesWebsearchSyntax(string term) => WebsearchOperators.IsMatch(term);

    /// <summary>
    /// The AND-of-lexemes <c>to_tsquery('simple', …)</c> input for a plain-words
    /// term, last token as a prefix — or null when the term contains no letters
    /// or digits at all (punctuation-only), meaning nothing can match.
    /// </summary>
    internal static string? BuildPrefixQuery(string term)
    {
        List<string> tokens = Tokenize(term);
        if (tokens.Count == 0)
        {
            return null;
        }

        var query = new StringBuilder();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (i > 0)
            {
                query.Append(" & ");
            }

            query.Append(tokens[i]);
        }

        return query.Append(":*").ToString();
    }

    /// <summary>
    /// Runs of letters/digits, splitting on everything else. Mirrors both the
    /// 'simple' regconfig (no stop words, no stemming) and the separator
    /// translation applied to <c>FileName</c> in the generated column, so C#
    /// tokens line up with the stored lexemes. Letter/digit-only tokens also
    /// need no escaping inside a <c>to_tsquery</c> input.
    /// </summary>
    private static List<string> Tokenize(string term)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (char c in term)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
