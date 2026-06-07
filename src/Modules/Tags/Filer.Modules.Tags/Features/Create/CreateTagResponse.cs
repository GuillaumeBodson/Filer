using Filer.Modules.Tags.Domain;

namespace Filer.Modules.Tags.Features.Create;

/// <summary>
/// The created tag, restated by this slice rather than shared with the future
/// list/rename slices so the contracts can evolve independently
/// (13-code-quality-and-design.md; same stance as the Folders slices).
/// </summary>
public sealed record CreateTagResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static CreateTagResponse From(Tag tag) => new(
        tag.Id,
        tag.Name,
        tag.CreatedAt,
        tag.UpdatedAt);
}
