using System.Text.Json.Serialization;

namespace Filer.Modules.Folders.Features.Update;

/// <summary>
/// The PATCH body with merge-patch semantics (03-api-specification.md): a field
/// that is absent is left untouched, while an explicit <c>"parentId": null</c>
/// moves the folder to the top level. System.Text.Json only invokes a setter for
/// properties present in the JSON, so the <c>Has*</c> flags capture exactly that
/// presence — a class with tracking setters instead of a record on purpose (same
/// pattern as Documents' update-metadata).
/// </summary>
public sealed class UpdateFolderRequest
{
    private string? _name;

    private Guid? _parentId;

    /// <summary>New display name; unique among active siblings per owner (02-data-model.md).</summary>
    public string? Name
    {
        get => _name;
        set
        {
            _name = value;
            HasName = true;
        }
    }

    /// <summary>New parent; explicit null = top level (02-data-model.md).</summary>
    public Guid? ParentId
    {
        get => _parentId;
        set
        {
            _parentId = value;
            HasParentId = true;
        }
    }

    /// <summary>Whether <see cref="Name"/> appeared in the request body.</summary>
    [JsonIgnore]
    public bool HasName { get; private set; }

    /// <summary>Whether <see cref="ParentId"/> appeared in the request body.</summary>
    [JsonIgnore]
    public bool HasParentId { get; private set; }
}
