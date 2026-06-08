namespace Filer.Modules.Tags.Features.Rename;

/// <summary>
/// The PATCH body (03-api-specification.md): a required new display name. Rename
/// touches the single mutable field of a tag, so unlike the Folders update slice
/// there is no merge-patch ambiguity — a missing name is simply invalid, the same
/// stance as the create slice. A plain record therefore suffices.
/// </summary>
public sealed record RenameTagRequest(string? Name);
