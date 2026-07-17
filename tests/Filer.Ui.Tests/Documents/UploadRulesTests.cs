using Filer.Ui.Documents;
using Filer.Ui.Models;
using FluentAssertions;
using Xunit;

namespace Filer.Ui.Tests.Documents;

public sealed class UploadRulesTests
{
    [Theory]
    [InlineData("report.pdf", "application/pdf", "application/pdf")]
    [InlineData("notes.md", "", "text/markdown")]
    [InlineData("notes.md", null, "text/markdown")]
    [InlineData("photo.JPG", "", "image/jpeg")]
    [InlineData("mystery.bin", "", null)]
    public void Resolves_the_content_type_from_browser_or_extension(
        string fileName, string? browserType, string? expected) =>
        UploadRules.ResolveContentType(fileName, browserType).Should().Be(expected);

    [Fact]
    public void An_oversized_file_fails_with_the_server_code()
    {
        ProblemDetailsView? problem = UploadRules.Validate("big.pdf", "application/pdf", sizeBytes: 11, maxSizeBytes: 10);

        problem!.Code.Should().Be("file_too_large");
        problem.Detail.Should().Contain("big.pdf");
    }

    [Fact]
    public void An_unsupported_type_fails_with_the_server_code()
    {
        ProblemDetailsView? problem = UploadRules.Validate("tool.exe", "application/x-msdownload", 10);

        problem!.Code.Should().Be("unsupported_file_type");
    }

    [Fact]
    public void An_unresolvable_type_fails_rather_than_uploading_blind()
    {
        UploadRules.Validate("mystery.bin", null, 10)!.Code.Should().Be("unsupported_file_type");
    }

    [Fact]
    public void A_valid_file_passes()
    {
        UploadRules.Validate("report.pdf", "application/pdf", 10).Should().BeNull();
    }
}
