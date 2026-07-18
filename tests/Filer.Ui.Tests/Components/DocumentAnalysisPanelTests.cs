using Bunit;
using Filer.ApiClient.Generated.Models;
using Microsoft.AspNetCore.Components;
using Filer.Ui.Components;
using Filer.Ui.Documents;
using Filer.Ui.Models;
using Filer.Ui.Tests.Documents;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.Ui.Tests.Components;

/// <summary>
/// The analysis-suggestions review panel (#141, 06-ai-analysis-pipeline.md): one
/// test per visual state, the all/some/none selection mechanics, and the apply
/// call with its error surface.
/// </summary>
public sealed class DocumentAnalysisPanelTests : BunitContext
{
    private static readonly Guid DocId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FolderId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly FakeDocumentsService _documents = new();

    public DocumentAnalysisPanelTests()
    {
        Services.AddSingleton<IDocumentsService>(_documents);
    }

    private static DocumentAnalysisResult Analysis(
        string status,
        AnalysisFolderSuggestionResponse? folder = null,
        params string[] tagNames) =>
        new(new DocumentAnalysisResponse
        {
            DocumentId = DocId,
            Status = status,
            Suggestions = status == "Succeeded"
                ? new DocumentAnalysisSuggestionsResponse
                {
                    SuggestedFolder = folder,
                    SuggestedTags = [.. tagNames.Select(name =>
                        new AnalysisTagSuggestionResponse { Name = name, Confidence = 0.9 })],
                }
                : null,
        }, null);

    private static AnalysisFolderSuggestionResponse ExistingFolder(string name = "Factures") =>
        new() { ExistingFolderId = FolderId, Name = name, Confidence = 0.82 };

    private IRenderedComponent<DocumentAnalysisPanel> RenderPanel(
        EventCallback onApplied = default) =>
        Render<DocumentAnalysisPanel>(ps =>
        {
            ps.Add(p => p.DocumentId, DocId);
            if (onApplied.HasDelegate)
            {
                ps.Add(p => p.OnApplied, onApplied);
            }
        });

    [Fact]
    public void No_analysis_renders_the_calm_empty_state()
    {
        _documents.AnalysisResults.Enqueue(Analysis("None"));

        var cut = RenderPanel();

        cut.WaitForAssertion(() =>
            cut.Find(".doc-analysis-empty").TextContent.Should().Contain("No analysis"));
    }

    [Theory]
    [InlineData("Queued")]
    [InlineData("Running")]
    public void A_pending_analysis_shows_progress_and_a_manual_refresh(string status)
    {
        _documents.AnalysisResults.Enqueue(Analysis(status));

        var cut = RenderPanel();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".doc-analysis-pending [role=status]").TextContent.Should().Contain("in progress");
            cut.Find(".doc-analysis-refresh").TextContent.Should().Contain("Refresh");
        });
    }

    [Fact]
    public void Refresh_reloads_the_analysis()
    {
        _documents.AnalysisResults.Enqueue(Analysis("Running"));
        _documents.AnalysisResults.Enqueue(Analysis("Succeeded", ExistingFolder(), "facture"));

        var cut = RenderPanel();
        cut.WaitForElement(".doc-analysis-refresh").Click();

        cut.WaitForAssertion(() =>
        {
            _documents.AnalysisCalls.Should().HaveCount(2);
            cut.FindAll(".doc-analysis-item").Should().HaveCount(2, "folder + one tag");
        });
    }

    [Fact]
    public void A_failed_analysis_reads_as_unavailable_and_never_blocks()
    {
        _documents.AnalysisResults.Enqueue(Analysis("Failed"));

        var cut = RenderPanel();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".doc-analysis-empty").TextContent.Should().Contain("unavailable");
            cut.FindAll("[role=alert]").Should().BeEmpty();
        });
    }

    [Fact]
    public void A_succeeded_analysis_without_suggestions_reads_as_nothing_to_suggest()
    {
        _documents.AnalysisResults.Enqueue(new DocumentAnalysisResult(
            new DocumentAnalysisResponse { DocumentId = DocId, Status = "Succeeded", Suggestions = null },
            null));

        var cut = RenderPanel();

        cut.WaitForAssertion(() =>
            cut.Find(".doc-analysis-empty").TextContent.Should().Contain("no suggestions"));
    }

    [Fact]
    public void A_load_problem_renders_the_error_state_with_retry()
    {
        _documents.AnalysisResults.Enqueue(new DocumentAnalysisResult(
            null, new ProblemDetailsView { Title = "Server error", Status = 500 }));

        var cut = RenderPanel();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Server error"));
    }

    [Fact]
    public void Suggestions_render_names_and_confidence()
    {
        _documents.AnalysisResults.Enqueue(Analysis("Succeeded", ExistingFolder(), "facture"));

        var cut = RenderPanel();

        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll(".doc-analysis-item");
            items.Should().HaveCount(2);
            items[0].TextContent.Should().Contain("Factures").And.Contain("82%");
            items[1].TextContent.Should().Contain("facture").And.Contain("90%");
        });
    }

    [Fact]
    public void Apply_is_disabled_until_something_is_selected()
    {
        _documents.AnalysisResults.Enqueue(Analysis("Succeeded", ExistingFolder(), "facture"));

        var cut = RenderPanel();

        cut.WaitForElement(".doc-analysis-apply").HasAttribute("disabled").Should().BeTrue();

        cut.FindAll(".doc-analysis-item input")[1].Change(true);

        cut.WaitForAssertion(() =>
            cut.Find(".doc-analysis-apply").HasAttribute("disabled").Should().BeFalse());
    }

    [Fact]
    public void A_proposed_new_folder_is_not_applicable_and_says_why()
    {
        var proposed = new AnalysisFolderSuggestionResponse
        {
            ExistingFolderId = null,
            Name = "Nouveau dossier",
            Confidence = 0.4,
        };
        _documents.AnalysisResults.Enqueue(Analysis("Succeeded", proposed, "facture"));

        var cut = RenderPanel();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".doc-analysis-item input")[0].HasAttribute("disabled").Should().BeTrue();
            cut.Find(".doc-analysis-note").TextContent.Should().Contain("doesn't exist yet");
        });
    }

    [Fact]
    public void Select_all_then_apply_sends_folder_and_every_tag()
    {
        _documents.AnalysisResults.Enqueue(Analysis("Succeeded", ExistingFolder(), "facture", "2026"));
        _documents.ApplyResults.Enqueue(new ApplyAnalysisResult(
            new ApplyDocumentAnalysisResponse { DocumentId = DocId, FolderApplied = true, FolderId = FolderId, Tags = [] },
            null));

        var cut = RenderPanel();
        cut.WaitForElement(".doc-analysis-bulk button").Click();
        cut.FindAll(".doc-analysis-item input")[0].Change(true);
        cut.Find(".doc-analysis-form").Submit();

        cut.WaitForAssertion(() =>
        {
            var apply = _documents.Applies.Should().ContainSingle().Which;
            apply.DocumentId.Should().Be(DocId);
            apply.ApplyFolder.Should().BeTrue();
            apply.Tags.Should().BeEquivalentTo(["facture", "2026"]);
            cut.Find(".doc-analysis-notice").TextContent.Should().Contain("applied");
        });
    }

    [Fact]
    public void Applying_a_subset_sends_only_the_checked_tags()
    {
        _documents.AnalysisResults.Enqueue(Analysis("Succeeded", folder: null, "facture", "2026"));
        _documents.ApplyResults.Enqueue(new ApplyAnalysisResult(
            new ApplyDocumentAnalysisResponse { DocumentId = DocId, FolderApplied = false, Tags = [] },
            null));

        var cut = RenderPanel();
        cut.WaitForElements(".doc-analysis-item input");
        cut.FindAll(".doc-analysis-item input")[0].Change(true);
        cut.Find(".doc-analysis-form").Submit();

        cut.WaitForAssertion(() =>
        {
            var apply = _documents.Applies.Should().ContainSingle().Which;
            apply.ApplyFolder.Should().BeFalse();
            apply.Tags.Should().BeEquivalentTo(["facture"]);
        });
    }

    [Fact]
    public void Clear_resets_the_selection()
    {
        _documents.AnalysisResults.Enqueue(Analysis("Succeeded", ExistingFolder(), "facture"));

        var cut = RenderPanel();
        cut.WaitForElement(".doc-analysis-bulk button").Click();
        cut.FindAll(".doc-analysis-bulk button")[1].Click();

        cut.WaitForAssertion(() =>
            cut.Find(".doc-analysis-apply").HasAttribute("disabled").Should().BeTrue());
    }

    [Fact]
    public void A_failed_apply_renders_the_problem_in_the_panel()
    {
        _documents.AnalysisResults.Enqueue(Analysis("Succeeded", folder: null, "facture"));
        _documents.ApplyResults.Enqueue(new ApplyAnalysisResult(null, new ProblemDetailsView
        {
            Title = "Validation failed",
            Detail = "Create the tag first, then re-apply.",
            Status = 400,
            Code = "suggested_tag_not_created",
        }));

        var cut = RenderPanel();
        cut.WaitForElements(".doc-analysis-item input");
        cut.FindAll(".doc-analysis-item input")[0].Change(true);
        cut.Find(".doc-analysis-form").Submit();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Create the tag first"));
    }

    [Fact]
    public void A_successful_apply_raises_OnApplied_and_clears_the_selection()
    {
        int applied = 0;
        _documents.AnalysisResults.Enqueue(Analysis("Succeeded", folder: null, "facture"));
        _documents.ApplyResults.Enqueue(new ApplyAnalysisResult(
            new ApplyDocumentAnalysisResponse { DocumentId = DocId, FolderApplied = false, Tags = [] },
            null));

        var cut = RenderPanel(EventCallback.Factory.Create(this, () => applied++));
        cut.WaitForElements(".doc-analysis-item input");
        cut.FindAll(".doc-analysis-item input")[0].Change(true);
        cut.Find(".doc-analysis-form").Submit();

        cut.WaitForAssertion(() =>
        {
            applied.Should().Be(1);
            cut.Find(".doc-analysis-apply").HasAttribute("disabled")
                .Should().BeTrue("the selection resets after a successful apply");
        });
    }
}
