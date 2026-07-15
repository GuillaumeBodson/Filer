using Filer.Modules.AiAnalysis.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Filer.Modules.AiAnalysis.Tests;

/// <summary>
/// Unit tests for <see cref="FakeAnalysisProvider"/> (#51): the canned-suggestion
/// behaviours the contract promises — prefer the user's existing organisation,
/// propose only when nothing exists, stay deterministic across runs (the worker's
/// idempotency leans on this — 06, Reliability), and honour cancellation.
/// </summary>
public sealed class FakeAnalysisProviderTests
{
    private readonly FakeAnalysisProvider _provider = new(NullLogger<FakeAnalysisProvider>.Instance);

    [Fact]
    public async Task AnalyzeAsync_suggests_the_first_existing_folder_by_name()
    {
        var work = new ExistingFolder(Guid.NewGuid(), "Work");
        var archive = new ExistingFolder(Guid.NewGuid(), "Archive");

        DocumentAnalysisResult result = await _provider.AnalyzeAsync(
            Request(folders: [work, archive]), TestContext.Current.CancellationToken);

        result.SuggestedFolder.Should().Be(
            new FolderSuggestion(archive.Id, "Archive", FakeAnalysisProvider.ExistingMatchConfidence),
            "the fake prefers the user's own folders, picked deterministically by name");
    }

    [Fact]
    public async Task AnalyzeAsync_proposes_a_new_folder_when_none_exist()
    {
        DocumentAnalysisResult result = await _provider.AnalyzeAsync(
            Request(folders: []), TestContext.Current.CancellationToken);

        result.SuggestedFolder.Should().Be(new FolderSuggestion(
            ExistingFolderId: null,
            FakeAnalysisProvider.ProposedFolderName,
            FakeAnalysisProvider.ProposedSuggestionConfidence),
            "a null ExistingFolderId marks a proposed folder, created only at apply time (06)");
    }

    [Fact]
    public async Task AnalyzeAsync_echoes_at_most_two_existing_tags_ordered_by_name()
    {
        DocumentAnalysisResult result = await _provider.AnalyzeAsync(
            Request(tags: ["taxes", "banking", "insurance"]), TestContext.Current.CancellationToken);

        result.SuggestedTags.Should().Equal(
            new TagSuggestion("banking", FakeAnalysisProvider.ExistingMatchConfidence),
            new TagSuggestion("insurance", FakeAnalysisProvider.ExistingMatchConfidence));
    }

    [Fact]
    public async Task AnalyzeAsync_proposes_the_file_extension_as_tag_when_no_tags_exist()
    {
        DocumentAnalysisResult result = await _provider.AnalyzeAsync(
            Request(fileName: "report.pdf", tags: []), TestContext.Current.CancellationToken);

        result.SuggestedTags.Should().Equal(
            new TagSuggestion("pdf", FakeAnalysisProvider.ProposedSuggestionConfidence));
    }

    [Fact]
    public async Task AnalyzeAsync_suggests_no_tags_when_no_tags_exist_and_the_file_has_no_extension()
    {
        DocumentAnalysisResult result = await _provider.AnalyzeAsync(
            Request(fileName: "README", tags: []), TestContext.Current.CancellationToken);

        result.SuggestedTags.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeAsync_reports_no_duplicate_signals()
    {
        DocumentAnalysisResult result = await _provider.AnalyzeAsync(
            Request(), TestContext.Current.CancellationToken);

        result.DuplicateSignals.Should().BeEmpty(
            "duplicate detection needs real content comparison; the fake never fabricates findings");
    }

    [Fact]
    public async Task AnalyzeAsync_is_deterministic_for_the_same_request()
    {
        DocumentAnalysisRequest request = Request(
            folders: [new ExistingFolder(Guid.NewGuid(), "Bills")],
            tags: ["household"]);

        DocumentAnalysisResult first = await _provider.AnalyzeAsync(request, TestContext.Current.CancellationToken);
        DocumentAnalysisResult second = await _provider.AnalyzeAsync(request, TestContext.Current.CancellationToken);

        second.Should().BeEquivalentTo(first,
            "re-running a job must produce a consistent result (06, Reliability — idempotency)");
    }

    [Fact]
    public async Task AnalyzeAsync_ignores_the_structural_folder_fields_and_stays_deterministic()
    {
        // #118 adds ParentId/DocumentCount for tree-aware providers; the fake must
        // keep producing the same canned suggestions regardless of their values.
        Guid workId = Guid.NewGuid();
        Guid archiveId = Guid.NewGuid();
        DocumentAnalysisResult flat = await _provider.AnalyzeAsync(
            Request(folders: [new ExistingFolder(workId, "Work"), new ExistingFolder(archiveId, "Archive")]),
            TestContext.Current.CancellationToken);
        DocumentAnalysisResult structural = await _provider.AnalyzeAsync(
            Request(folders:
            [
                new ExistingFolder(workId, "Work", ParentId: null, DocumentCount: 12),
                new ExistingFolder(archiveId, "Archive", ParentId: workId, DocumentCount: 7),
            ]),
            TestContext.Current.CancellationToken);

        structural.Should().BeEquivalentTo(flat,
            "the fake ignores ParentId and DocumentCount, so enriching the context changes nothing (#118)");
    }

    [Fact]
    public async Task AnalyzeAsync_throws_when_already_cancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => _provider.AnalyzeAsync(Request(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "providers honour cancellation — deleting a document cancels its analysis (06)");
    }

    private static DocumentAnalysisRequest Request(
        string fileName = "invoice.pdf",
        IReadOnlyList<ExistingFolder>? folders = null,
        IReadOnlyList<string>? tags = null) =>
        new(
            DocumentId: Guid.NewGuid(),
            FileName: fileName,
            ContentType: "application/pdf",
            Text: "Lorem ipsum.",
            ExistingFolders: folders ?? [],
            ExistingTags: tags ?? []);
}
