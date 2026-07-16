using System.Net;
using System.Text;
using System.Text.Json;
using Filer.Modules.AiAnalysis.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Filer.Modules.AiAnalysis.Tests;

/// <summary>
/// Unit tests for <see cref="OllamaAnalysisProvider"/> (#52): the no-egress adapter
/// is exercised against a stubbed <see cref="HttpMessageHandler"/> — no real Ollama,
/// no network. They pin the contract behaviours (06, Provider Abstraction): mapping
/// the model reply to the neutral result, the outgoing request shape, and the
/// infrastructure failure semantics (throw, so the worker retries — 13).
/// </summary>
public sealed class OllamaAnalysisProviderTests
{
    private const string BaseUrl = "http://localhost:11434";

    [Fact]
    public async Task AnalyzeAsync_WithWellFormedReply_ResolvesExistingFolder()
    {
        var work = new ExistingFolder(Guid.NewGuid(), "Work");
        var archive = new ExistingFolder(Guid.NewGuid(), "Archive");
        StubHandler handler = RespondingWith(Reply(
            folderName: "archive",
            folderConfidence: 0.91,
            tags: [("invoice", 0.8), ("invoice", 0.7), ("2026", 0.6)]));

        OllamaAnalysisProvider provider = Provider(handler);

        DocumentAnalysisResult result = await provider.AnalyzeAsync(
            Request(folders: [work, archive]), TestContext.Current.CancellationToken);

        result.SuggestedFolder.Should().Be(
            new FolderSuggestion(archive.Id, "Archive", 0.91),
            "an existing folder name (case-insensitive) resolves to its id and canonical name");
        result.SuggestedTags.Should().Equal(
            new TagSuggestion("invoice", 0.8),
            new TagSuggestion("2026", 0.6));
        result.DuplicateSignals.Should().BeEmpty("duplicate detection is not the LLM's job in V1");
    }

    [Fact]
    public async Task AnalyzeAsync_WithUnknownFolderReply_ProposesNewFolderAndClampsConfidences()
    {
        StubHandler handler = RespondingWith(Reply(
            folderName: "Receipts",
            folderConfidence: 1.7,
            tags: [("scan", -0.4)]));

        OllamaAnalysisProvider provider = Provider(handler);

        DocumentAnalysisResult result = await provider.AnalyzeAsync(
            Request(folders: []), TestContext.Current.CancellationToken);

        result.SuggestedFolder.Should().Be(
            new FolderSuggestion(ExistingFolderId: null, "Receipts", 1.0),
            "an unknown name is a proposed folder and confidence is clamped to [0,1]");
        result.SuggestedTags.Should().Equal(new TagSuggestion("scan", 0.0));
    }

    [Fact]
    public async Task AnalyzeAsync_WhenTextEmpty_WorksFromFileNameAlone()
    {
        StubHandler handler = RespondingWith(Reply("Unsorted", 0.4, []));
        OllamaAnalysisProvider provider = Provider(handler);

        DocumentAnalysisResult result = await provider.AnalyzeAsync(
            Request(fileName: "scan.pdf", text: string.Empty), TestContext.Current.CancellationToken);

        result.SuggestedFolder!.Name.Should().Be("Unsorted");
        handler.LastPrompt.Should().Contain("scan.pdf");
    }

    [Fact]
    public async Task AnalyzeAsync_Always_PostsToApiChatWithStreamFalseFormatSchemaAndModel()
    {
        StubHandler handler = RespondingWith(Reply("Work", 0.5, []));
        OllamaAnalysisProvider provider = Provider(handler);

        await provider.AnalyzeAsync(
            Request(folders: [new ExistingFolder(Guid.NewGuid(), "Work")], tags: ["taxes"]),
            TestContext.Current.CancellationToken);

        handler.LastRequestUri.Should().NotBeNull();
        handler.LastRequestUri!.AbsolutePath.Should().Be("/api/chat");

        using JsonDocument body = JsonDocument.Parse(handler.LastRequestBody!);
        JsonElement root = body.RootElement;
        root.GetProperty("model").GetString().Should().Be("llama3.2:3b");
        root.GetProperty("stream").GetBoolean().Should().BeFalse();
        root.TryGetProperty("format", out JsonElement format).Should().BeTrue("the reply is constrained by a JSON schema");
        format.GetProperty("type").GetString().Should().Be("object");

        handler.LastPrompt.Should().Contain("invoice.pdf").And.Contain("Work").And.Contain("taxes");
    }

    [Fact]
    public async Task AnalyzeAsync_WithFolderTree_RendersTreeWithDocumentCountsInPrompt()
    {
        // #118: structural context — the prompt carries the owner's tree (children
        // indented under their parent) and per-folder document counts, so the model
        // suggests into the existing organisation.
        var work = new ExistingFolder(Guid.NewGuid(), "Work", ParentId: null, DocumentCount: 2);
        var invoices = new ExistingFolder(Guid.NewGuid(), "Invoices", work.Id, DocumentCount: 1);
        var archive = new ExistingFolder(Guid.NewGuid(), "Archive", ParentId: null, DocumentCount: 0);
        StubHandler handler = RespondingWith(Reply("Work", 0.5, []));
        OllamaAnalysisProvider provider = Provider(handler);

        await provider.AnalyzeAsync(
            Request(folders: [archive, invoices, work]), TestContext.Current.CancellationToken);

        handler.LastPrompt.Should().Contain(
            "\n- Archive (0 documents)",
            "top-level folders render as list roots with their counts");
        handler.LastPrompt.Should().Contain(
            "\n- Work (2 documents)\n  - Invoices (1 document)",
            "a child renders indented under its parent, wherever it sits in the flat input");
    }

    [Fact]
    public async Task AnalyzeAsync_WithLongText_TruncatesToMaxPromptChars()
    {
        StubHandler handler = RespondingWith(Reply("Work", 0.5, []));
        const int maxPromptChars = 50;
        OllamaAnalysisProvider provider = Provider(handler, maxPromptChars: maxPromptChars);

        // 'z' is absent from the prompt scaffolding (unlike 'x', which appears in
        // "existing"/"text"), so counting it isolates the document-text run.
        string longText = new('z', 500);
        await provider.AnalyzeAsync(
            Request(text: longText), TestContext.Current.CancellationToken);

        int runLength = handler.LastPrompt!.Count(character => character == 'z');
        runLength.Should().Be(maxPromptChars, "document text is truncated to the configured prompt budget");
    }

    [Fact]
    public async Task AnalyzeAsync_OnNonSuccessStatus_Throws()
    {
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        OllamaAnalysisProvider provider = Provider(handler);

        Func<Task> act = () => provider.AnalyzeAsync(Request(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>(
            "a non-2xx response is an infrastructure failure the worker retries (13)");
    }

    [Fact]
    public async Task AnalyzeAsync_OnUnparseableBody_Throws()
    {
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all", Encoding.UTF8, "application/json"),
        });
        OllamaAnalysisProvider provider = Provider(handler);

        Func<Task> act = () => provider.AnalyzeAsync(Request(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>("an unparseable reply cannot be mapped, so the run fails");
    }

    [Fact]
    public async Task AnalyzeAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        StubHandler handler = RespondingWith(Reply("Work", 0.5, []));
        OllamaAnalysisProvider provider = Provider(handler);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = () => provider.AnalyzeAsync(Request(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "providers honour cancellation mid-flight (06)");
    }

    private static OllamaAnalysisProvider Provider(StubHandler handler, int maxPromptChars = 8_000)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl, UriKind.Absolute) };
        var options = Options.Create(new AiAnalysisOptions
        {
            Provider = AiAnalysisOptions.OllamaProviderName,
            Ollama = new OllamaOptions { MaxPromptChars = maxPromptChars },
        });

        return new OllamaAnalysisProvider(httpClient, options, NullLogger<OllamaAnalysisProvider>.Instance);
    }

    private static StubHandler RespondingWith(string replyJson) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(replyJson, Encoding.UTF8, "application/json"),
        });

    /// <summary>Builds an Ollama /api/chat response whose message content is the structured suggestion JSON.</summary>
    private static string Reply(
        string folderName, double folderConfidence, IReadOnlyList<(string Name, double Confidence)> tags)
    {
        string tagJson = string.Join(",", tags.Select(tag =>
            $$"""{"name":"{{tag.Name}}","confidence":{{tag.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"""));
        string content =
            $$"""{"folder":{"name":"{{folderName}}","confidence":{{folderConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},"tags":[{{tagJson}}]}""";

        // The content field carries the model's reply as a JSON-encoded string.
        string encodedContent = JsonSerializer.Serialize(content);
        return "{\"message\":{\"role\":\"assistant\",\"content\":" + encodedContent + "}}";
    }

    private static DocumentAnalysisRequest Request(
        string fileName = "invoice.pdf",
        string text = "Lorem ipsum.",
        IReadOnlyList<ExistingFolder>? folders = null,
        IReadOnlyList<string>? tags = null) =>
        new(
            DocumentId: Guid.NewGuid(),
            FileName: fileName,
            ContentType: "application/pdf",
            Text: text,
            ExistingFolders: folders ?? [],
            ExistingTags: tags ?? []);

    /// <summary>
    /// A test <see cref="HttpMessageHandler"/> that captures the outgoing request and
    /// returns a canned response — no socket is opened.
    /// </summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        public string? LastRequestBody { get; private set; }

        public string? LastPrompt { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LastRequestUri = request.RequestUri;
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
                using JsonDocument body = JsonDocument.Parse(LastRequestBody);
                LastPrompt = body.RootElement
                    .GetProperty("messages")[0]
                    .GetProperty("content")
                    .GetString();
            }

            return responder(request);
        }
    }
}
