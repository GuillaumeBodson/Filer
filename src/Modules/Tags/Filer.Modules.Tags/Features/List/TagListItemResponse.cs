using Filer.Modules.Tags.Domain;

namespace Filer.Modules.Tags.Features.List;

/// <summary>
/// One tag in the response of <c>GET /api/v1/tags</c> (03-api-specification.md):
/// an explicit DTO, never the entity — internals such as <c>OwnerId</c> stay
/// server-side (05-security.md). Restated by this slice rather than shared with
/// the create slice so the contracts can evolve independently
/// (13-code-quality-and-design.md; same stance as the Folders slices).
/// </summary>
public sealed record TagListItemResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// The slice's single entity → DTO projection (13-code-quality-and-design.md:
    /// explicit projection mapping, owned by the slice).
    /// </summary>
    public static TagListItemResponse From(Tag tag) => new(
        tag.Id,
        tag.Name,
        tag.CreatedAt,
        tag.UpdatedAt);
}
