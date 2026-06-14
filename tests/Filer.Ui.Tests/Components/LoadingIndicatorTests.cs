using Bunit;
using Filer.Ui.Components;
using FluentAssertions;
using Xunit;

namespace Filer.Ui.Tests.Components;

public sealed class LoadingIndicatorTests : BunitContext
{
    [Fact]
    public void Renders_accessible_status_with_default_message()
    {
        var cut = Render<LoadingIndicator>();

        var status = cut.Find("[role=status]");
        status.GetAttribute("aria-live").Should().Be("polite");
        status.TextContent.Should().Contain("Loading");
    }

    [Fact]
    public void Renders_custom_message()
    {
        var cut = Render<LoadingIndicator>(ps => ps.Add(p => p.Message, "Fetching documents"));

        cut.Find("[role=status]").TextContent.Should().Contain("Fetching documents");
    }
}
