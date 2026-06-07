using System.Text.Json.Serialization;

namespace Filer.Modules.Documents.Features.UpdateMetadata;

/// <summary>
/// The PATCH body with merge-patch semantics (03-api-specification.md): a field
/// that is absent is left untouched, while an explicit <c>"folderId": null</c>
/// moves the document to the root. System.Text.Json only invokes a setter for
/// properties present in the JSON, so the <c>Has*</c> flags capture exactly that
/// presence — a class with tracking setters instead of a record on purpose.
/// </summary>
public sealed class UpdateDocumentMetadataRequest
{
    private string? _fileName;

    private Guid? _folderId;

    /// <summary>New original file name; metadata only, never a storage path (05-security.md).</summary>
    public string? FileName
    {
        get => _fileName;
        set
        {
            _fileName = value;
            HasFileName = true;
        }
    }

    /// <summary>Target folder; explicit null = root / unfiled (02-data-model.md).</summary>
    public Guid? FolderId
    {
        get => _folderId;
        set
        {
            _folderId = value;
            HasFolderId = true;
        }
    }

    /// <summary>Whether <see cref="FileName"/> appeared in the request body.</summary>
    [JsonIgnore]
    public bool HasFileName { get; private set; }

    /// <summary>Whether <see cref="FolderId"/> appeared in the request body.</summary>
    [JsonIgnore]
    public bool HasFolderId { get; private set; }
}
