namespace Filer.SharedKernel.Results;

/// <summary>
/// The standard error shape. Maps to the RFC 7807-style problem details returned
/// by the API (03-api-specification.md). <see cref="Type"/> categorises the
/// failure so the host can choose the right HTTP status.
/// </summary>
public sealed record Error(ErrorType Type, string Code, string Message)
{
    public static Error Validation(string message, string code = "validation") =>
        new(ErrorType.Validation, code, message);

    public static Error Unauthorized(string message = "Authentication required.", string code = "unauthorized") =>
        new(ErrorType.Unauthorized, code, message);

    public static Error NotFound(string message = "Resource not found.", string code = "not_found") =>
        new(ErrorType.NotFound, code, message);

    public static Error Conflict(string message, string code = "conflict") =>
        new(ErrorType.Conflict, code, message);

    public static Error Unexpected(string message = "An unexpected error occurred.", string code = "unexpected") =>
        new(ErrorType.Unexpected, code, message);
}

/// <summary>Categories of failure, mapped to HTTP status codes by the host.</summary>
public enum ErrorType
{
    Validation,
    Unauthorized,
    NotFound,
    Conflict,
    Unexpected,
}
