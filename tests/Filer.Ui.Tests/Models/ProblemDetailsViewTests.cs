using Filer.Ui.Models;
using FluentAssertions;
using Xunit;

namespace Filer.Ui.Tests.Models;

public sealed class ProblemDetailsViewTests
{
    [Fact]
    public void TryParse_reads_all_fields_including_validation_errors()
    {
        const string json = """
        {
          "type": "https://docs/errors/file_name_invalid",
          "title": "Validation failed",
          "status": 400,
          "detail": "FileName is required.",
          "code": "file_name_invalid",
          "errors": { "fileName": ["required", "too short"] }
        }
        """;

        var problem = ProblemDetailsView.TryParse(json);

        problem.Should().NotBeNull();
        problem!.Type.Should().Be("https://docs/errors/file_name_invalid");
        problem.Title.Should().Be("Validation failed");
        problem.Code.Should().Be("file_name_invalid");
        problem.Status.Should().Be(400);
        problem.Detail.Should().Be("FileName is required.");
        problem.HasValidationErrors.Should().BeTrue();
        problem.Errors.Should().ContainKey("fileName");
        problem.Errors["fileName"].Should().BeEquivalentTo("required", "too short");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    public void TryParse_returns_null_for_blank_or_non_object_input(string? input)
    {
        ProblemDetailsView.TryParse(input).Should().BeNull();
    }

    [Fact]
    public void IsNotFound_is_true_only_for_404()
    {
        ProblemDetailsView.ForStatus(404).IsNotFound.Should().BeTrue();
        ProblemDetailsView.ForStatus(400).IsNotFound.Should().BeFalse();
        new ProblemDetailsView().IsNotFound.Should().BeFalse();
    }

    [Fact]
    public void ForStatus_carries_status_and_optional_text()
    {
        var problem = ProblemDetailsView.ForStatus(500, "Server error", "Something broke.");

        problem.Status.Should().Be(500);
        problem.Title.Should().Be("Server error");
        problem.Detail.Should().Be("Something broke.");
        problem.HasValidationErrors.Should().BeFalse();
    }

    [Fact]
    public void Parsed_body_without_errors_has_no_validation_errors()
    {
        var problem = ProblemDetailsView.TryParse("""{ "title": "Conflict", "status": 409 }""");

        problem.Should().NotBeNull();
        problem!.HasValidationErrors.Should().BeFalse();
        problem.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parsed_body_without_code_extension_has_null_code()
    {
        var problem = ProblemDetailsView.TryParse("""{ "title": "Conflict", "status": 409 }""");

        problem.Should().NotBeNull();
        problem!.Code.Should().BeNull();
    }
}
