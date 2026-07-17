using Filer.Modules.Documents.Contracts;
using Filer.SharedKernel.Results;
using Filer.WebKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Filer.Modules.Documents.Features.Upload;

public static class UploadDocumentEndpoint
{
    public static void MapUploadDocument(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("", async (
            IFormFile? file,
            UploadDocumentService service,
            CancellationToken ct) =>
        {
            if (file is null)
            {
                return Error.Validation(
                        "A multipart/form-data request with a 'file' part is required.",
                        DocumentsErrorCodes.FileRequired)
                    .ToHttpResult();
            }

            // The host buffers the multipart section, so the stream is seekable —
            // required for the sniff → hash → store passes in the service.
            await using Stream content = file.OpenReadStream();
            var command = new UploadDocumentCommand(file.FileName, file.ContentType, file.Length, content);

            Result<UploadDocumentResult> result = await service.HandleAsync(command, ct);
            if (result.IsFailure)
            {
                return result.Error!.ToHttpResult();
            }

            UploadDocumentResult outcome = result.Value;
            if (outcome.DuplicateOfDocumentId is Guid existingDocumentId)
            {
                // 409 carrying the existing document's reference so the client can
                // decide whether to proceed (03-api-specification.md, upload behavior).
                return Results.Problem(
                    title: "Conflict",
                    detail: "A document with identical content already exists.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: $"https://docs/errors/{DocumentsErrorCodes.DuplicateContent}",
                    extensions: new Dictionary<string, object?>
                    {
                        [ErrorResults.CodeExtension] = DocumentsErrorCodes.DuplicateContent,
                        ["existingDocumentId"] = existingDocumentId.ToString(),
                    });
            }

            UploadDocumentResponse document = outcome.Document!;
            return Results.Created($"{DocumentsRoutes.BasePath}/{document.Id}", document);
        })
        .WithName("UploadDocument")
        .Produces<UploadDocumentResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
        .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
        // Bearer-token API with no cookie authentication: CSRF does not apply, and
        // antiforgery tokens are unobtainable for non-browser clients (05-security.md
        // scopes auth to JWTs). Without this, minimal APIs reject IFormFile binding.
        .DisableAntiforgery()
        .RequireAuthorization();
    }
}
