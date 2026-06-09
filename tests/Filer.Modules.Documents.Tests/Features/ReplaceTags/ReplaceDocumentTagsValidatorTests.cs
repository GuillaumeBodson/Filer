using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Features.ReplaceTags;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.ReplaceTags;

/// <summary>Every structural rule of the replace body, pinned at the validator
/// (12-testing-strategy.md).</summary>
public sealed class ReplaceDocumentTagsValidatorTests
{
    [Fact]
    public void Validate_NullTagIds_FailsTagIdsInvalid()
    {
        Result result = ReplaceDocumentTagsValidator.Validate(new ReplaceDocumentTagsRequest(null));
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(DocumentsErrorCodes.TagIdsInvalid);
    }

    [Fact]
    public void Validate_EmptyTagIds_Succeeds()
    {
        // An empty array is legitimate: it clears the document's User tags.
        ReplaceDocumentTagsValidator.Validate(new ReplaceDocumentTagsRequest([])).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_ContainsEmptyGuid_FailsTagIdsInvalid()
    {
        Result result = ReplaceDocumentTagsValidator.Validate(new ReplaceDocumentTagsRequest([Guid.Empty]));
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(DocumentsErrorCodes.TagIdsInvalid);
    }

    [Fact]
    public void Validate_ValidIds_Succeeds() =>
        ReplaceDocumentTagsValidator.Validate(new ReplaceDocumentTagsRequest([Guid.NewGuid()]))
            .IsSuccess.Should().BeTrue();
}
