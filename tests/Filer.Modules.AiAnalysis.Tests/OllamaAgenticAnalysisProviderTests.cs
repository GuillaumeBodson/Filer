using System.Net;
using System.Text;
using System.Text.Json;
using Filer.Modules.AiAnalysis.Contracts;
using Filer.Modules.Documents.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Filer.Modules.AiAnalysis.Tests;

/// <summary>
/// Unit tests for the experimental <see cref="OllamaAgenticAnalysisProvider"/>
/// (#119) against a stubbed <see cref="HttpMessageHandler"/> and a mocked
/// <see cref="IFolderContentLookup"/> — no real Ollama, no database. They pin the
/// two-pass loop (rank → sample → confirm), the owner-scoping of every mid-analysis
/// lookup (05), the single-pass fallback when nothing is inspectable, cancellation
/// across both passes (06), and the plain-adapter failure semantics (throw, so the
/// worker retries — 13).
/// </summary>
public sealed class OllamaAgenticAnalysisProviderTests
{
    private const string BaseUrl = "http://localhost:11434";

    private readonly Mock<IFolderContentLookup> _lookup = new();

    public OllamaAgenticAnalysisProviderTests() =>
        _lookup
            .Setup(l => l.GetFolderSampleAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

    [Fact]
    public async Task AnalyzeAsync_happy_path_ranks_samples_and_confirms_an_existing_folder()
    {
        var work = new ExistingFolder(Guid.NewGuid(), "Work");
        var invoices = new ExistingFolder(Guid.NewGuid(), "Invoices");
        DocumentAnalysisRequest request = Request(folders: [work, invoices]);
        _lookup
            .Setup(l => l.GetFolderSampleAsync(
                request.OwnerId, invoices.Id, OllamaAgenticAnalysisProvider.FolderSampleSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["facture-edf.pdf", "aws-invoice.pdf"]);
        SequenceHandler handler = new(
            CandidateReply(("invoices", 0.8), ("Receipts", 0.5)),
            ConfirmationReply("invoices", 0.95));

        DocumentAnalysisResult result = await Provider(handler).AnalyzeAsync(
            request, TestContext.Current.CancellationToken);

        result.SuggestedFolder.Should().Be(
            new FolderSuggestion(invoices.Id, "Invoices", 0.95),
            "the confirmed candidate resolves to the existing folder's id and canonical name");
        result.SuggestedTags.Should().Equal([new TagSuggestion("invoice", 0.9)],
            "tag suggestions come from the ranking pass");
        result.DuplicateSignals.Should().BeEmpty();

        handler.Prompts.Should().HaveCount(2, "an inspectable candidate triggers the confirmation pass");
        handler.Prompts[1].Should().Contain("Invoices").And.Contain("facture-edf.pdf").And.Contain("aws-invoice.pdf");
        _lookup.Verify(
            l => l.GetFolderSampleAsync(
                request.OwnerId, invoices.Id, OllamaAgenticAnalysisProvider.FolderSampleSize, It.IsAny<CancellationToken>()),
            Times.Once,
            "every mid-analysis read is keyed by the request's owner (05, #119)");
        _lookup.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnalyzeAsync_skips_the_confirmation_pass_when_no_candidate_matches_an_existing_folder()
    {
        SequenceHandler handler = new(CandidateReply(("Receipts", 1.4)));

        DocumentAnalysisResult result = await Provider(handler).AnalyzeAsync(
            Request(folders: [new ExistingFolder(Guid.NewGuid(), "Work")]),
            TestContext.Current.CancellationToken);

        result.SuggestedFolder.Should().Be(
            new FolderSuggestion(ExistingFolderId: null, "Receipts", 1.0),
            "with nothing to inspect, the top candidate becomes a plain clamped proposal");
        handler.Prompts.Should().HaveCount(1, "no existing candidate means no second model call");
        _lookup.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnalyzeAsync_suggests_no_folder_when_the_ranking_pass_returns_no_candidates()
    {
        SequenceHandler handler = new(CandidateReply());

        DocumentAnalysisResult result = await Provider(handler).AnalyzeAsync(
            Request(folders: []), TestContext.Current.CancellationToken);

        result.SuggestedFolder.Should().BeNull();
        result.SuggestedTags.Should().Equal(new TagSuggestion("invoice", 0.9));
    }

    [Fact]
    public async Task AnalyzeAsync_inspects_at_most_the_top_three_existing_candidates()
    {
        ExistingFolder[] folders = [.. Enumerable.Range(1, 4)
            .Select(i => new ExistingFolder(Guid.NewGuid(), $"Folder{i}"))];
        DocumentAnalysisRequest request = Request(folders: folders);
        SequenceHandler handler = new(
            CandidateReply(("Folder1", 0.9), ("Folder2", 0.8), ("Folder3", 0.7), ("Folder4", 0.6)),
            ConfirmationReply("Folder1", 0.9));

        await Provider(handler).AnalyzeAsync(request, TestContext.Current.CancellationToken);

        _lookup.Verify(
            l => l.GetFolderSampleAsync(request.OwnerId, It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Exactly(OllamaAgenticAnalysisProvider.MaxCandidates),
            "the loop is bounded: only the top 1–3 candidates are inspected (#119)");
        _lookup.Verify(
            l => l.GetFolderSampleAsync(It.IsAny<Guid>(), folders[3].Id, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AnalyzeAsync_honours_cancellation_before_the_first_pass()
    {
        SequenceHandler handler = new(CandidateReply(("Work", 0.5)));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => Provider(handler).AnalyzeAsync(Request(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "providers honour cancellation mid-flight (06)");
    }

    [Fact]
    public async Task AnalyzeAsync_honours_cancellation_between_the_two_passes()
    {
        var work = new ExistingFolder(Guid.NewGuid(), "Work");
        using var cts = new CancellationTokenSource();
        // Cancel while the sampling step runs: the second model call must never happen.
        _lookup
            .Setup(l => l.GetFolderSampleAsync(
                It.IsAny<Guid>(), work.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid _, Guid _, int _, CancellationToken token) =>
            {
                await cts.CancelAsync();
                token.ThrowIfCancellationRequested();
                return (IReadOnlyList<string>)[];
            });
        SequenceHandler handler = new(
            CandidateReply(("Work", 0.9)),
            ConfirmationReply("Work", 0.9));

        Func<Task> act = () => Provider(handler).AnalyzeAsync(Request(folders: [work]), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "cancellation is honoured across both passes (06, #119)");
        handler.Prompts.Should().HaveCount(1, "the confirmation pass must not run after cancellation");
    }

    [Fact]
    public async Task AnalyzeAsync_throws_on_a_non_success_status()
    {
        var handler = new SequenceHandler();
        handler.EnqueueRaw(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Func<Task> act = () => Provider(handler).AnalyzeAsync(Request(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>(
            "a non-2xx response is an infrastructure failure the worker retries (13)");
    }

    [Fact]
    public async Task AnalyzeAsync_throws_on_an_unparseable_confirmation_body()
    {
        var handler = new SequenceHandler(CandidateReply(("Work", 0.9)));
        handler.EnqueueRaw(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all", Encoding.UTF8, "application/json"),
        });

        Func<Task> act = () => Provider(handler).AnalyzeAsync(
            Request(folders: [new ExistingFolder(Guid.NewGuid(), "Work")]),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>("an unparseable second-pass reply fails the run for retry");
    }

    private OllamaAgenticAnalysisProvider Provider(SequenceHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl, UriKind.Absolute) };
        var options = Options.Create(new AiAnalysisOptions
        {
            Provider = AiAnalysisOptions.OllamaAgenticProviderName,
            Ollama = new OllamaOptions(),
        });

        return new OllamaAgenticAnalysisProvider(
            httpClient, _lookup.Object, options, NullLogger<OllamaAgenticAnalysisProvider>.Instance);
    }

    private static DocumentAnalysisRequest Request(
        IReadOnlyList<ExistingFolder>? folders = null,
        IReadOnlyList<string>? tags = null) =>
        new(
            DocumentId: Guid.NewGuid(),
            FileName: "invoice.pdf",
            ContentType: "application/pdf",
            Text: "Lorem ipsum.",
            ExistingFolders: folders ?? [],
            ExistingTags: tags ?? [],
            OwnerId: Guid.NewGuid());

    /// <summary>Ranking-pass reply: candidates (best first) plus one fixed tag suggestion.</summary>
    private static string CandidateReply(params (string Name, double Confidence)[] candidates)
    {
        string candidateJson = string.Join(",", candidates.Select(c =>
            $$"""{"name":"{{c.Name}}","confidence":{{c.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"""));
        string content =
            $$"""{"candidates":[{{candidateJson}}],"tags":[{"name":"invoice","confidence":0.9}]}""";

        return WrapAsChatResponse(content);
    }

    /// <summary>Confirmation-pass reply: the single chosen folder.</summary>
    private static string ConfirmationReply(string folderName, double confidence)
    {
        string content = "{\"folder\":{\"name\":\"" + folderName + "\",\"confidence\":"
            + confidence.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}";

        return WrapAsChatResponse(content);
    }

    private static string WrapAsChatResponse(string content) =>
        "{\"message\":{\"role\":\"assistant\",\"content\":" + JsonSerializer.Serialize(content) + "}}";

    /// <summary>
    /// A test <see cref="HttpMessageHandler"/> that replays queued responses in
    /// order and captures every outgoing prompt — the two-pass loop shows up as two
    /// captured prompts. No socket is opened.
    /// </summary>
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public SequenceHandler(params string[] replyBodies)
        {
            foreach (string body in replyBodies)
            {
                _responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
            }
        }

        public List<string> Prompts { get; } = [];

        public void EnqueueRaw(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Content is not null)
            {
                string body = await request.Content.ReadAsStringAsync(cancellationToken);
                using JsonDocument parsed = JsonDocument.Parse(body);
                Prompts.Add(parsed.RootElement
                    .GetProperty("messages")[0]
                    .GetProperty("content")
                    .GetString()!);
            }

            _responses.Count.Should().BePositive("the provider made more HTTP calls than the test scripted");
            return _responses.Dequeue();
        }
    }
}
