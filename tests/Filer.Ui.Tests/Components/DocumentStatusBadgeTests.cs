using Bunit;
using Filer.Ui.Components;
using FluentAssertions;
using Xunit;

namespace Filer.Ui.Tests.Components;

public sealed class DocumentStatusBadgeTests : BunitContext
{
    [Theory]
    [InlineData("Ready", "status-ok", "Ready")]
    [InlineData("Analyzing", "status-run", "Analyzing…")]
    [InlineData("Failed", "status-err", "Analysis failed")]
    [InlineData("Uploaded", "status-neutral", "Uploaded")]
    [InlineData("SomethingNew", "status-neutral", "SomethingNew")]
    public void Maps_status_to_variant_and_label(string status, string expectedClass, string expectedLabel)
    {
        var cut = Render<DocumentStatusBadge>(ps => ps.Add(p => p.Status, status));

        var badge = cut.Find(".status");
        badge.ClassList.Should().Contain(expectedClass);
        badge.TextContent.Should().Be(expectedLabel);
    }
}
