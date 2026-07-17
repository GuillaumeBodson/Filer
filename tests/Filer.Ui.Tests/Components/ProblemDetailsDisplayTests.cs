using Bunit;
using Filer.Ui.Components;
using Filer.Ui.Models;
using FluentAssertions;
using Xunit;

namespace Filer.Ui.Tests.Components;

public sealed class ProblemDetailsDisplayTests : BunitContext
{
    [Fact]
    public void Headlines_the_problem_title_when_present()
    {
        var cut = Render<ProblemDetailsDisplay>(ps => ps.Add(
            p => p.Problem,
            ProblemDetailsView.ForStatus(400, "Validation failed", "FileName is required.")));

        cut.Find(".problem-title").TextContent.Should().Be("Validation failed");
        cut.Find(".problem-detail").TextContent.Should().Be("FileName is required.");
    }

    [Fact]
    public void Falls_back_to_a_status_code_title_when_the_title_is_missing()
    {
        var cut = Render<ProblemDetailsDisplay>(ps => ps.Add(
            p => p.Problem, ProblemDetailsView.ForStatus(500)));

        cut.Find(".problem-title").TextContent.Should().Be("Request failed (500)");
    }

    [Fact]
    public void Falls_back_to_a_generic_title_without_title_or_status()
    {
        var cut = Render<ProblemDetailsDisplay>(ps => ps.Add(
            p => p.Problem, new ProblemDetailsView()));

        cut.Find(".problem-title").TextContent.Should().Be("Request failed");
    }

    [Fact]
    public void Omits_detail_and_errors_when_absent()
    {
        var cut = Render<ProblemDetailsDisplay>(ps => ps.Add(
            p => p.Problem, ProblemDetailsView.ForStatus(500)));

        cut.FindAll(".problem-detail").Should().BeEmpty();
        cut.FindAll(".problem-errors").Should().BeEmpty();
    }
}
