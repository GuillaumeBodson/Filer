using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Filer.Api.Infrastructure;

/// <summary>
/// Collapses nullable complex properties — emitted by the .NET OpenAPI generator
/// as <c>oneOf [{"type":"null"}, {"$ref": …}]</c> — into the bare <c>$ref</c>.
/// Kiota turns such discriminator-less unions into composed-type wrappers whose
/// deserializer cannot pick a branch, silently dropping the payload (the
/// <c>DocumentAnalysisResponse.suggestions</c> case, #141). A plain <c>$ref</c>
/// generates an ordinary nullable property the client reads correctly — the same
/// contract-quality-for-client-quality trade as the strict-number serializer
/// option in Program.cs (ADR-011). The wire format is unchanged; only the
/// schema's nullability annotation on complex properties is lost.
/// </summary>
internal sealed class NullableRefSchemaTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Components?.Schemas is not { } schemas)
        {
            return Task.CompletedTask;
        }

        foreach (IOpenApiSchema componentSchema in schemas.Values)
        {
            if (componentSchema is not OpenApiSchema { Properties: { } properties })
            {
                continue;
            }

            foreach (string name in properties.Keys.ToList())
            {
                if (properties[name] is OpenApiSchema { OneOf: { Count: 2 } oneOf }
                    && oneOf.Any(IsNullSchema)
                    && oneOf.OfType<OpenApiSchemaReference>().SingleOrDefault() is { } reference)
                {
                    properties[name] = reference;
                }
            }
        }

        return Task.CompletedTask;
    }

    private static bool IsNullSchema(IOpenApiSchema schema) =>
        schema is OpenApiSchema { Type: JsonSchemaType.Null };
}
