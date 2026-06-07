namespace Filer.Modules.Documents.Features.Upload;

/// <summary>
/// Input to the upload slice, expressed in primitives rather than HTTP types so
/// the service stays unit-testable without the web stack (12-testing-strategy.md).
/// <paramref name="Content"/> must be readable and seekable — the endpoint hands
/// over the buffered multipart section, which is both.
/// </summary>
/// <param name="FileName">Original client file name; stored as metadata only (05).</param>
/// <param name="ContentType">Declared MIME type, validated against the allow-list and magic bytes (04/05).</param>
/// <param name="SizeBytes">Exact file size as buffered by the host.</param>
/// <param name="Content">The file bytes. The caller owns the stream's lifetime.</param>
public sealed record UploadDocumentCommand(
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content);
