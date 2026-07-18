using Filer.Modules.Documents.Persistence;
using FluentAssertions;
using Xunit;

namespace Filer.Modules.Documents.Tests.Persistence;

/// <summary>
/// The tsquery strategy behind <c>EfOwnerDocumentSearch</c> (#57): which terms
/// are handed to <c>websearch_to_tsquery</c>, and how plain-word terms become an
/// AND-of-lexemes prefix query. The SQL both feed into runs against Postgres in
/// Filer.IntegrationTests.
/// </summary>
public sealed class SearchTermTsQueryTests
{
    [Theory]
    [InlineData("\"facture 2024\"")]
    [InlineData("facture -2023")]
    [InlineData("-brouillon")]
    [InlineData("facture or reçu")]
    [InlineData("facture OR reçu")]
    [InlineData("or")]
    public void UsesWebsearchSyntax_DetectsWebsearchOperators(string term)
    {
        SearchTermTsQuery.UsesWebsearchSyntax(term).Should().BeTrue();
    }

    [Theory]
    [InlineData("facture")]
    [InlineData("facture 2024")]
    [InlineData("my-file")]
    [InlineData("rapport_final.pdf")]
    [InlineData("horde")]
    [InlineData("minor")]
    public void UsesWebsearchSyntax_LeavesPlainTermsAlone(string term)
    {
        // A dash inside a word is a file-name separator, not an exclusion, and
        // 'or' only operates as a standalone word.
        SearchTermTsQuery.UsesWebsearchSyntax(term).Should().BeFalse();
    }

    [Theory]
    [InlineData("facture", "facture:*")]
    [InlineData("facture 2024", "facture & 2024:*")]
    [InlineData("facture_2024.pdf", "facture & 2024 & pdf:*")]
    [InlineData("my-file", "my & file:*")]
    [InlineData("  fact  ", "fact:*")]
    [InlineData("électricité", "électricité:*")]
    public void BuildPrefixQuery_JoinsLexemesWithAndAndPrefixesTheLastToken(string term, string expected)
    {
        SearchTermTsQuery.BuildPrefixQuery(term).Should().Be(expected);
    }

    [Theory]
    [InlineData("...")]
    [InlineData("!!!")]
    [InlineData("  .  ")]
    public void BuildPrefixQuery_WithNoLettersOrDigits_ReturnsNull(string term)
    {
        SearchTermTsQuery.BuildPrefixQuery(term).Should().BeNull();
    }
}
