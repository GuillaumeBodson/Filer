using Filer.SharedKernel.Results;
using Microsoft.AspNetCore.Http;

namespace Filer.Modules.Auth.Web;

/// <summary>
/// Maps a domain <see cref="Error"/> to an RFC 7807-style problem result
/// (03-api-specification.md). Lives in the module for now; if a second module
/// needs the identical mapping it can be promoted to a shared web kernel via ADR
/// (10-solution-structure.md — duplication is preferred over premature sharing).
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
            _ => StatusCodes.Status500InternalServerError,
        };

        return Results.Problem(
            title: error.Code,
            detail: error.Message,
            statusCode: status,
            type: $"https://docs/errors/{error.Code}");
    }
}
