using System.Text.Json;
using Filer.Modules.Folders.Features.Update;
using FluentAssertions;
using Xunit;

namespace Filer.Modules.Folders.Tests.Features.Update;

/// <summary>
/// Pins the merge-patch contract the request DTO is built on: System.Text.Json
/// invokes a setter only for properties present in the JSON, so the <c>Has*</c>
/// flags distinguish "absent" from "explicitly null" — the difference between
/// "leave the parent alone" and "move to the top level"
/// (03-api-specification.md; same pattern as Documents' update-metadata).
/// </summary>
public sealed class UpdateFolderRequestTests
{
    /// <summary>The endpoint's serializer settings (minimal APIs use web defaults).</summary>
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Deserialize_EmptyBody_MarksNothingProvided()
    {
        var request = JsonSerializer.Deserialize<UpdateFolderRequest>("{}", Web)!;

        request.HasName.Should().BeFalse();
        request.HasParentId.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_NameOnly_MarksOnlyNameProvided()
    {
        var request = JsonSerializer.Deserialize<UpdateFolderRequest>(
            """{"name":"Renamed"}""", Web)!;

        request.HasName.Should().BeTrue();
        request.Name.Should().Be("Renamed");
        request.HasParentId.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_ExplicitNullParentId_MarksParentProvidedWithNullTarget()
    {
        var request = JsonSerializer.Deserialize<UpdateFolderRequest>(
            """{"parentId":null}""", Web)!;

        request.HasParentId.Should().BeTrue();
        request.ParentId.Should().BeNull("explicit null means move to the top level (02-data-model.md)");
        request.HasName.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_ParentIdValue_MarksParentProvidedWithThatTarget()
    {
        var parentId = Guid.NewGuid();

        var request = JsonSerializer.Deserialize<UpdateFolderRequest>(
            $$"""{"parentId":"{{parentId}}"}""", Web)!;

        request.HasParentId.Should().BeTrue();
        request.ParentId.Should().Be(parentId);
    }

    [Fact]
    public void Deserialize_ExplicitNullName_MarksNameProvided()
    {
        // Present-but-null must be visible to the validator so it can reject it:
        // a folder cannot exist without a name.
        var request = JsonSerializer.Deserialize<UpdateFolderRequest>(
            """{"name":null}""", Web)!;

        request.HasName.Should().BeTrue();
        request.Name.Should().BeNull();
    }
}
