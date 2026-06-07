using System.Text.Json;
using Filer.Modules.Documents.Features.UpdateMetadata;
using FluentAssertions;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.UpdateMetadata;

/// <summary>
/// Pins the merge-patch contract the request DTO is built on: System.Text.Json
/// invokes a setter only for properties present in the JSON, so the <c>Has*</c>
/// flags distinguish "absent" from "explicitly null" — the difference between
/// "leave the folder alone" and "move to root" (03-api-specification.md).
/// </summary>
public sealed class UpdateDocumentMetadataRequestTests
{
    /// <summary>The endpoint's serializer settings (minimal APIs use web defaults).</summary>
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Deserialize_EmptyBody_MarksNothingProvided()
    {
        var request = JsonSerializer.Deserialize<UpdateDocumentMetadataRequest>("{}", Web)!;

        request.HasFileName.Should().BeFalse();
        request.HasFolderId.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_FileNameOnly_MarksOnlyFileNameProvided()
    {
        var request = JsonSerializer.Deserialize<UpdateDocumentMetadataRequest>(
            """{"fileName":"renamed.pdf"}""", Web)!;

        request.HasFileName.Should().BeTrue();
        request.FileName.Should().Be("renamed.pdf");
        request.HasFolderId.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_ExplicitNullFolderId_MarksFolderProvidedWithNullTarget()
    {
        var request = JsonSerializer.Deserialize<UpdateDocumentMetadataRequest>(
            """{"folderId":null}""", Web)!;

        request.HasFolderId.Should().BeTrue();
        request.FolderId.Should().BeNull("explicit null means move to root (02-data-model.md)");
        request.HasFileName.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_FolderIdValue_MarksFolderProvidedWithThatTarget()
    {
        var folderId = Guid.NewGuid();

        var request = JsonSerializer.Deserialize<UpdateDocumentMetadataRequest>(
            $$"""{"folderId":"{{folderId}}"}""", Web)!;

        request.HasFolderId.Should().BeTrue();
        request.FolderId.Should().Be(folderId);
    }

    [Fact]
    public void Deserialize_ExplicitNullFileName_MarksFileNameProvided()
    {
        // Present-but-null must be visible to the validator so it can reject it:
        // a document cannot exist without a file name.
        var request = JsonSerializer.Deserialize<UpdateDocumentMetadataRequest>(
            """{"fileName":null}""", Web)!;

        request.HasFileName.Should().BeTrue();
        request.FileName.Should().BeNull();
    }
}
