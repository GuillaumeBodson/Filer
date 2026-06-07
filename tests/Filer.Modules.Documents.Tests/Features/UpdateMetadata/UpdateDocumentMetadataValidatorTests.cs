using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.Modules.Documents.Features.UpdateMetadata;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.UpdateMetadata;

public sealed class UpdateDocumentMetadataValidatorTests
{
    [Fact]
    public void Validate_NothingProvided_FailsAsEmptyUpdate()
    {
        var request = new UpdateDocumentMetadataRequest();

        Result result = UpdateDocumentMetadataValidator.Validate(request);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.UpdateEmpty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ProvidedFileNameMissingOrBlank_FailsAsInvalidFileName(string? fileName)
    {
        var request = new UpdateDocumentMetadataRequest { FileName = fileName };

        Result result = UpdateDocumentMetadataValidator.Validate(request);

        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(DocumentsErrorCodes.FileNameInvalid);
    }

    [Fact]
    public void Validate_FileNameOverTheColumnBound_FailsAsInvalidFileName()
    {
        var request = new UpdateDocumentMetadataRequest
        {
            FileName = new string('a', Document.MaxFileNameLength + 1),
        };

        Result result = UpdateDocumentMetadataValidator.Validate(request);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(DocumentsErrorCodes.FileNameInvalid);
    }

    [Fact]
    public void Validate_FileNameExactlyAtTheBound_Succeeds()
    {
        var request = new UpdateDocumentMetadataRequest
        {
            FileName = new string('a', Document.MaxFileNameLength),
        };

        UpdateDocumentMetadataValidator.Validate(request).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_FolderMoveAlone_Succeeds()
    {
        var request = new UpdateDocumentMetadataRequest { FolderId = Guid.NewGuid() };

        UpdateDocumentMetadataValidator.Validate(request).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_MoveToRootAlone_Succeeds()
    {
        // Explicit null is a legal target — the root (02-data-model.md). Folder
        // ownership for non-null targets is the service's check, not structural
        // validation.
        var request = new UpdateDocumentMetadataRequest { FolderId = null };

        UpdateDocumentMetadataValidator.Validate(request).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_RenameAndMoveTogether_Succeeds()
    {
        var request = new UpdateDocumentMetadataRequest
        {
            FileName = "renamed.pdf",
            FolderId = Guid.NewGuid(),
        };

        UpdateDocumentMetadataValidator.Validate(request).IsSuccess.Should().BeTrue();
    }
}
