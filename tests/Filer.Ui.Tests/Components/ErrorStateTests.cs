using Bunit;
using Filer.Ui.Components;
using Filer.Ui.Models;
using FluentAssertions;
using Xunit;

namespace Filer.Ui.Tests.Components;

public sealed class ErrorStateTests : BunitContext
{
    private static ProblemDetailsView Validation() => new()
    {
        Title = "Validation failed",
        Status = 400,
        Detail = "FileName is required.",
        Errors = new Dictionary<string, IReadOnlyList<string>>
        {
            ["fileName"] = ["required"],
        },
    };

    [Fact]
    public void Renders_alert_with_title_and_detail_for_non_404()
    {
        var cut = Render<ErrorState>(ps => ps.Add(p => p.Problem, Validation()));

        var alert = cut.Find("[role=alert]");
        alert.TextContent.Should().Contain("Validation failed");
        alert.TextContent.Should().Contain("FileName is required.");
    }

    [Fact]
    public void Renders_validation_errors()
    {
        var cut = Render<ErrorState>(ps => ps.Add(p => p.Problem, Validation()));

        cut.Find(".problem-errors").TextContent.Should().Contain("required");
    }

    [Fact]
    public void Surfaces_404_as_not_found_rather_than_an_alert()
    {
        var cut = Render<ErrorState>(ps => ps
            .Add(p => p.Problem, ProblemDetailsView.ForStatus(404))
            .Add(p => p.NotFoundTitle, "Document not found"));

        cut.FindAll("[role=alert]").Should().BeEmpty();
        cut.Find(".empty-title").TextContent.Should().Be("Document not found");
    }

    [Fact]
    public void Shows_retry_button_and_invokes_callback_when_handler_set()
    {
        var retried = false;
        var cut = Render<ErrorState>(ps => ps
            .Add(p => p.Problem, Validation())
            .Add(p => p.OnRetry, () => retried = true));

        cut.Find(".error-retry").Click();

        retried.Should().BeTrue();
    }

    [Fact]
    public void Hides_retry_button_when_no_handler()
    {
        var cut = Render<ErrorState>(ps => ps.Add(p => p.Problem, Validation()));

        cut.FindAll(".error-retry").Should().BeEmpty();
    }
}
