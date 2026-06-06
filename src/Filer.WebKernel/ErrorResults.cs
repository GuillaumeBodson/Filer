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
    public static IResult ToHttpResult(this Error error)
    {
        int status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
            ErrorType.UnsupportedMediaType => StatusCodes.Status415UnsupportedMediaType,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Results.Problem(
            title: error.Code,
            detail: error.Message,
            statusCode: status,
            type: $"https://docs/errors/{error.Code}");
    }
}
