using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Filer.IntegrationTests.Infrastructure;

/// <summary>
/// Test-side accessors for the problem-details contract (03-api-specification.md,
/// #169): the machine-readable error code travels in the <c>code</c> extension
/// member, not in <c>title</c>.
/// </summary>
public static class ProblemDetailsExtensions
{
    /// <summary>
    /// Reads the <c>code</c> extension member, or <c>null</c> when absent.
    /// System.Text.Json materialises unknown extension values as
    /// <see cref="JsonElement"/>.
    /// </summary>
    public static string? Code(this ProblemDetails problem) =>
        problem.Extensions.TryGetValue("code", out object? value) && value is JsonElement element
            ? element.GetString()
            : null;
}
