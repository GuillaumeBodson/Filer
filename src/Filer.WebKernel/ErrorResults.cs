using Filer.SharedKernel.Results;
using Microsoft.AspNetCore.Http;

namespace Filer.WebKernel;

/// <summary>
/// Maps a domain <see cref="Error"/> to an RFC 7807-style problem result
/// (03-api-specification.md). Promoted from the Auth module to this shared web
/// kernel (ADR-006) so every module's endpoints map failures identically — the
/// second consumer the original in-module note anticipated has arrived.
/// </summary>
public static class ErrorResults
{
    /// <summary>
    /// The extension member carrying the machine-readable error code
    /// (e.g. <c>invalid_credentials</c>). Clients branch on this; <c>title</c> is
    /// reserved for the human-readable summary (#169).
    /// </summary>
    public const string CodeExtension = "code";

    public static IResult ToHttpResult(this Error error)
    {
        (int status, string title) = error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "Validation failed"),
            ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "Authentication failed"),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Resource not found"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            ErrorType.PayloadTooLarge => (StatusCodes.Status413PayloadTooLarge, "Payload too large"),
            ErrorType.UnsupportedMediaType => (StatusCodes.Status415UnsupportedMediaType, "Unsupported media type"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };

        return Results.Problem(
            title: title,
            detail: error.Message,
            statusCode: status,
            type: $"https://docs/errors/{error.Code}",
            extensions: new Dictionary<string, object?> { [CodeExtension] = error.Code });
    }
}
