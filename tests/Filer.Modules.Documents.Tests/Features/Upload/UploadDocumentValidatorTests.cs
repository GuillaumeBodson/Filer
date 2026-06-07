using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Features.Upload;
using Filer.SharedKernel.Results;
using FluentAssertions;
using Xunit;

namespace Filer.Modules.Documents.Tests.Features.Upload;

public sealed class UploadDocumentValidatorTests
{
    private readonly DocumentsOptions _options = new();

    private static UploadDocumentCommand Command(
        string fileName = "notes.txt", string contentType = "text/plain", long sizeBytes = 10) =>
        new(fileName, contentType, sizeBytes, Stream.Null);

    [Fact]
    public void Validate_WithDefaultsAndAllowedType_Succeeds() =>
        UploadDocumentValidator.Validate(Command(), _options).IsSuccess.Should().BeTrue();

    [Fact]
    public void Validate_NormalizesContentTypeParametersAndCase()
    {
        Result result = UploadDocumentValidator.Validate(
            Command(contentType: "Text/Plain; charset=utf-8"), _options);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WhenSizeNotPositive_FailsWithFileRequired(long sizeBytes)
    {
        Result result = UploadDocumentValidator.Validate(Command(sizeBytes: sizeBytes), _options);

        result.Error!.Code.Should().Be(DocumentsErrorCodes.FileRequired);
    }

    [Fact]
    public void Validate_WhenFileNameTooLong_FailsWithFileNameInvalid()
    {
        string longName = new('a', UploadDocumentValidator.MaxFileNameLength + 1);

        Result result = UploadDocumentValidator.Validate(Command(fileName: longName), _options);

        result.Error!.Code.Should().Be(DocumentsErrorCodes.FileNameInvalid);
    }

    [Fact]
    public void Validate_WhenOverConfiguredMaximum_FailsWithPayloadTooLarge()
    {
        _options.MaxUploadBytes = 5;

        Result result = UploadDocumentValidator.Validate(Command(sizeBytes: 6), _options);

        result.Error!.Type.Should().Be(ErrorType.PayloadTooLarge);
        result.Error.Code.Should().Be(DocumentsErrorCodes.FileTooLarge);
    }

    [Fact]
    public void Validate_WhenTypeNotInAllowList_FailsWithUnsupportedFileType()
    {
        Result result = UploadDocumentValidator.Validate(
            Command(contentType: "application/zip"), _options);

        result.Error!.Type.Should().Be(ErrorType.UnsupportedMediaType);
        result.Error.Code.Should().Be(DocumentsErrorCodes.UnsupportedFileType);
    }
}
