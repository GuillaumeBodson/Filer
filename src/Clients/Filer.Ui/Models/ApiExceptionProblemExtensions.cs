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

    // Extension members deserialize as UntypedNode (Kiota's representation of
    // undeclared JSON); the "code" member is the machine-readable error code (#169).
    private static string? ReadString(IDictionary<string, object>? data, string key) =>
        data is not null && data.TryGetValue(key, out object? value)
            ? value switch
            {
                UntypedString untyped => untyped.GetValue(),
                string text => text,
                _ => null,
            }
            : null;
}
