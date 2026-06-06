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

    public static Error PayloadTooLarge(string message, string code = "payload_too_large") =>
        new(ErrorType.PayloadTooLarge, code, message);

    public static Error UnsupportedMediaType(string message, string code = "unsupported_media_type") =>
        new(ErrorType.UnsupportedMediaType, code, message);

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

    /// <summary>A request or file exceeds a configured size limit (04-non-functional.md) — 413.</summary>
    PayloadTooLarge,

    /// <summary>A declared or sniffed file type falls outside the allow-list (04/05) — 415.</summary>
    UnsupportedMediaType,

    Unexpected,
}
