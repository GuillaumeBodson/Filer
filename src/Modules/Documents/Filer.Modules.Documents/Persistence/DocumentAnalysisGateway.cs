using Filer.Modules.Documents.Contracts;
using Filer.Modules.Documents.Domain;
using Filer.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Filer.Modules.Documents.Persistence;

/// <summary>
/// The module's implementation of the analysis read/advance contract, directly
/// over the module-owned context: the worker has no caller principal, so the
/// owner-scoped <c>IDocumentStore</c> seam deliberately cannot serve it — this
/// gateway is the one read path keyed by document id alone, and it still excludes
/// soft-deleted rows so a deleted document reads as gone (06-ai-analysis-pipeline.md).
/// </summary>
internal sealed class DocumentAnalysisGateway(DocumentsDbContext db, IClock clock) : IDocumentAnalysisGateway
{
    public Task<AnalysisDocumentSnapshot?> FindForAnalysisAsync(
        Guid documentId, CancellationToken cancellationToken) =>
        db.Documents
            .AsNoTracking()
            .Where(d => d.Id == documentId && d.DeletedAt == null)
            .Select(d => new AnalysisDocumentSnapshot(d.Id, d.OwnerId, d.FileName, d.ContentType, d.StorageKey))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task MarkReadyAsync(Guid documentId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.UtcNow;

        // Set-based and guarded on the row still being live: a document deleted
        // while its analysis finished is left alone (its job is being cancelled by
        // the delete slice), and re-marking Ready is a harmless overwrite — the
        // idempotency the pipeline requires (06, Reliability).
        await db.Documents
            .Where(d => d.Id == documentId && d.DeletedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.Status, DocumentStatus.Ready)
                    .SetProperty(d => d.UpdatedAt, now),
                cancellationToken);
    }
}
