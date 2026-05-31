using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Filer.Api.Infrastructure;

/// <summary>
/// Backstop for exceptions that are not expected business outcomes. Expected
/// outcomes flow through <c>Result</c>/<c>Error</c> and are mapped per slice
/// (03-api-specification.md, 13-code-quality-and-design.md); this handler catches
/// what bubbles past that — infrastructure failures and broken invariants.
///
/// It logs the full detail server-side (with the request's trace id) and returns
/// the standard problem-details shape to the client, never a stack trace
/// (05-security.md). Known infrastructure exceptions get a meaningful status; the
/// rest are a 500.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService = problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Read the request coordinates once into locals. Both are cheap, and
        // passing locals (rather than an inline PathString->string conversion)
        // keeps the log calls free of argument evaluation the logger might
        // discard (CA1873).
        string method = httpContext.Request.Method;
        string path = httpContext.Request.Path;

        // A client that disconnected mid-request is not a server error: don't log
        // it as one and don't try to write a body to a connection that's gone.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.ClientCancelled(method, path);
            return true;
        }

        (int status, string code, string detail) = exception switch
        {
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "conflict",
                "The resource was modified concurrently. Reload and try again."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "unexpected",
                "An unexpected error occurred."),
        };

        // Full detail to the logs only. The trace id is included via the logging
        // scope ASP.NET Core attaches to the request.
        _logger.UnhandledException(exception, method, path);

        httpContext.Response.StatusCode = status;

        // Same problem-details shape as the per-slice mapping (ErrorResults):
        // title = error code, type = the error's doc URI.
        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = code,
                Detail = detail,
                Type = $"https://docs/errors/{code}",
            },
        });
    }
}

/// <summary>
/// Log messages for <see cref="GlobalExceptionHandler"/>, co-located with it in a
/// dedicated <c>static partial class</c> (house convention — 13-code-quality-and-design.md).
/// Compile-time-generated and allocation-free via the <c>[LoggerMessage]</c> source
/// generator (CA1848); EventIds are namespaced by the calling logger's category, so
/// they never collide with other types' messages.
/// </summary>
internal static partial class GlobalExceptionHandlerLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Request {Method} {Path} was cancelled by the client.")]
    public static partial void ClientCancelled(this ILogger logger, string method, string path);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Unhandled exception handling {Method} {Path}.")]
    public static partial void UnhandledException(this ILogger logger, Exception exception, string method, string path);
}
