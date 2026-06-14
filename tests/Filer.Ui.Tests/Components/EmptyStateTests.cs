using Bunit;
using Filer.Ui.Components;
using FluentAssertions;
using Xunit;

namespace Filer.Ui.Tests.Components;

public sealed class EmptyStateTests : BunitContext
{
    [Fact]
    public void Renders_title()
    {
        var cut = Render<EmptyState>(ps => ps.Add(p => p.Title, "No documents yet"));

        cut.Find(".empty-title").TextContent.Should().Be("No documents yet");
    }

    [Fact]
    public void Renders_description_when_provided()
    {
        var cut = Render<EmptyState>(ps => ps
            .Add(p => p.Title, "No documents yet")
            .Add(p => p.Description, "Upload your first file to get started."));

        cut.Find(".empty-description").TextContent.Should().Contain("Upload your first file");
    }

    [Fact]
    public void Omits_description_when_not_provided()
    {
        var cut = Render<EmptyState>(ps => ps.Add(p => p.Title, "No documents yet"));

        cut.FindAll(".empty-description").Should().BeEmpty();
    }

    [Fact]
    public void Renders_child_content_actions()
    {
        var cut = Render<EmptyState>(ps => ps
            .Add(p => p.Title, "No documents yet")
            .AddChildContent("<button>Upload</button>"));

        cut.Find(".empty-actions button").TextContent.Should().Be("Upload");
    }
}
