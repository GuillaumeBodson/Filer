using Filer.Modules.Tags.Domain;

namespace Filer.Modules.Tags.Features.Rename;

/// <summary>
/// The renamed tag, restated by this slice rather than shared with the other tag
/// slices so the contracts can evolve independently
/// (13-code-quality-and-design.md; same stance as the create slice).
/// </summary>
public sealed record RenameTagResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static RenameTagResponse From(Tag tag) => new(
        tag.Id,
        tag.Name,
        tag.CreatedAt,
        tag.UpdatedAt);
}
