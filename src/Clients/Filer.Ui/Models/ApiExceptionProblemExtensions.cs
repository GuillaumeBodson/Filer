using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using GeneratedProblemDetails = Filer.ApiClient.Generated.Models.ProblemDetails;

namespace Filer.Ui.Models;

/// <summary>
/// Bridges the Kiota-typed error surface to the UI's <see cref="ProblemDetailsView"/>.
/// Declared error statuses deserialize into the generated <c>ProblemDetails</c> model
/// (#146); anything else falls back to a status-only view.
/// </summary>
public static class ApiExceptionProblemExtensions
{
    public static ProblemDetailsView ToProblemView(this ApiException exception)
    {
        int? status = exception.ResponseStatusCode == 0 ? null : exception.ResponseStatusCode;

        if (exception is not GeneratedProblemDetails problem)
        {
            return new ProblemDetailsView { Status = status };
        }

        return new ProblemDetailsView
        {
            Type = problem.Type,
            Title = problem.Title,
            Detail = problem.Detail,
            Status = status,
            Code = ReadString(problem.AdditionalData, "code"),
        };
    }

    /// <summary>
    /// Reads a string-valued extension member from a typed problem response, e.g. the
    /// upload 409's <c>existingDocumentId</c>. <c>null</c> when absent or not a string.
    /// </summary>
    public static string? GetExtensionString(this ApiException exception, string key) =>
        exception is GeneratedProblemDetails problem ? ReadString(problem.AdditionalData, key) : null;

    // Extension members deserialize as UntypedNode or eagerly-typed primitives -
    // Kiota's JSON parser turns GUID-shaped strings into boxed Guids, so the upload
    // 409's existingDocumentId arrives as a Guid, while "code" (#169) stays a string.
    private static string? ReadString(IDictionary<string, object>? data, string key) =>
        data is not null && data.TryGetValue(key, out object? value)
            ? value switch
            {
                UntypedString untyped => untyped.GetValue(),
                string text => text,
                Guid guid => guid.ToString(),
                _ => null,
            }
            : null;
}
