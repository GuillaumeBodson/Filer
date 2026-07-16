using System.Text.Json;
using Filer.Modules.AiAnalysis.Contracts;
using FluentAssertions;
using Xunit;

namespace Filer.Modules.AiAnalysis.Tests;

/// <summary>
/// The shared serialization contract for <c>AnalysisJob.Result</c> (#53): the
/// worker writes it, the status/apply slices read it, so the round trip and the
/// exact wire conventions (web defaults — camelCase, absent-field tolerant) are
/// asserted here once for both sides.
/// </summary>
public sealed class AnalysisJobResultJsonTests
{
    private static DocumentAnalysisResult FullResult()
    {
        var folderId = new Guid("11111111-1111-1111-1111-111111111111");

        return new DocumentAnalysisResult(
            new FolderSuggestion(folderId, "Invoices", 0.85),
            [new TagSuggestion("invoice", 0.9), new TagSuggestion("2026", 0.4)]);
    }

    [Fact]
    public void SerializeThenDeserialize_FullShape_RoundTrips()
    {
        DocumentAnalysisResult original = FullResult();

        DocumentAnalysisResult? roundTripped = AnalysisJobResultJson.Deserialize(
            AnalysisJobResultJson.Serialize(original));

        roundTripped.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void SerializeThenDeserialize_NoFolderAndEmptyTags_RoundTrips()
    {
        var original = new DocumentAnalysisResult(SuggestedFolder: null, []);

        DocumentAnalysisResult? roundTripped = AnalysisJobResultJson.Deserialize(
            AnalysisJobResultJson.Serialize(original));

        roundTripped.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Serialize_Always_UsesWebDefaultsCamelCase()
    {
        // The wire shape is a contract shared with every reader of the JSONB
        // column — pin its conventions so a converter or naming change cannot
        // slip in silently.
        string json = AnalysisJobResultJson.Serialize(FullResult());

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        root.TryGetProperty("suggestedFolder", out JsonElement folder).Should().BeTrue();
        folder.GetProperty("existingFolderId").GetGuid()
            .Should().Be(new Guid("11111111-1111-1111-1111-111111111111"));
        folder.GetProperty("name").GetString().Should().Be("Invoices");
        folder.GetProperty("confidence").GetDouble().Should().Be(0.85);

        root.GetProperty("suggestedTags")[0].GetProperty("name").GetString().Should().Be("invoice");
    }

    [Fact]
    public void Deserialize_LegacyPayloadCarryingDuplicateSignals_IgnoresTheRemovedField()
    {
        // Rows persisted before duplicate signals were dropped (#164) still carry
        // the field; the contract must keep reading them.
        const string legacyJson =
            """{"suggestedFolder":null,"suggestedTags":[{"name":"invoice","confidence":0.5}],"duplicateSignals":[]}""";

        DocumentAnalysisResult? result = AnalysisJobResultJson.Deserialize(legacyJson);

        result.Should().NotBeNull();
        result.SuggestedTags.Should().ContainSingle().Which.Name.Should().Be("invoice");
    }

    [Fact]
    public void Deserialize_JsonNullLiteral_ReturnsNull()
    {
        AnalysisJobResultJson.Deserialize("null").Should().BeNull();
    }
}
