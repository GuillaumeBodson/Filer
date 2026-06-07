namespace Filer.Modules.Tags.Features.Create;

/// <summary>
/// The POST body (03-api-specification.md): a required display name. Creation has
/// no merge-patch semantics, so a plain record suffices — same stance as the
/// Folders create slice.
/// </summary>
public sealed record CreateTagRequest(string? Name);
