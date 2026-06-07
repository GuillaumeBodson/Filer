namespace Filer.Modules.Documents.Features.DownloadContent;

/// <summary>
/// What the endpoint needs to stream the blob back: the open content stream and
/// the metadata that shapes the response headers. The endpoint owns the stream's
/// lifetime — ASP.NET Core disposes it after writing the response body.
/// </summary>
public sealed record DownloadDocumentContentResult(
    Stream Content,
    string ContentType,
    string FileName,
    long SizeBytes);
