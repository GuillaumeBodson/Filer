using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Domain;
using Filer.Modules.Folders.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Folders.Features.Update;

/// <summary>
/// The rename/move slice (03-api-specification.md): validate the patch, resolve
/// the caller's folder, verify the move target, refuse cycles, enforce sibling
/// uniqueness, apply. Cross-owner, missing, and soft-deleted folders are a
/// uniform 404 (05-security.md) — and so is a target parent the caller does not
/// own. Cycles are prevented in application logic (02-data-model.md): a folder
/// may never become its own ancestor, so re-parenting under itself or any of its
/// descendants is a 409. The sibling check is the business 409; the partial
/// unique index on (OwnerId, ParentId, Name) is the race-condition backstop.
/// </summary>
public sealed class UpdateFolderService(
    IFolderStore folders,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<UpdateFolderService> logger)
{
    public async Task<Result<UpdateFolderResponse>> HandleAsync(
        Guid folderId, UpdateFolderRequest request, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership filters below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<UpdateFolderResponse>(Error.Unauthorized());
        }

        Result validation = UpdateFolderValidator.Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<UpdateFolderResponse>(validation.Error!);
        }

        // One owner-scoped, soft-delete-aware lookup: anything it does not return
        // is a uniform 404 (05-security.md).
        Folder? folder = await folders.FindActiveByIdAsync(
            currentUser.Id, folderId, cancellationToken);
        if (folder is null)
        {
            return Result.Failure<UpdateFolderResponse>(
                Error.NotFound("The folder was not found.", FoldersErrorCodes.FolderNotFound));
        }

        // The validator guarantees a non-empty trimmed name when present; the
        // untouched half of the patch keeps its current value (merge-patch).
        string newName = request.HasName ? request.Name!.Trim() : folder.Name;
        Guid? newParentId = request.HasParentId ? request.ParentId : folder.ParentId;

        // A non-null target must be an active folder the caller owns (uniform 404,
        // 05-security.md) and must not be the folder itself or one of its
        // descendants (cycle prevention in application logic, 02-data-model.md).
        // Explicit null is the top level and needs neither check.
        if (request.HasParentId && request.ParentId is Guid targetParentId)
        {
            if (!await folders.ActiveExistsAsync(currentUser.Id, targetParentId, cancellationToken))
            {
                return Result.Failure<UpdateFolderResponse>(
                    Error.NotFound("The parent folder was not found.", FoldersErrorCodes.ParentNotFound));
            }

            if (await WouldCreateCycleAsync(folder.Id, targetParentId, cancellationToken))
            {
                return Result.Failure<UpdateFolderResponse>(Error.Conflict(
                    "A folder cannot be moved under itself or one of its descendants.",
                    FoldersErrorCodes.MoveCycle));
            }
        }

        // Only a changed (parent, name) pair can collide: the current pair would
        // only ever match the folder's own row, which is not a conflict.
        bool changed = newName != folder.Name || newParentId != folder.ParentId;
        if (changed && await folders.ActiveSiblingNameExistsAsync(
                currentUser.Id, newParentId, newName, cancellationToken))
        {
            return Result.Failure<UpdateFolderResponse>(Error.Conflict(
                "A folder with the same name already exists at this level.",
                FoldersErrorCodes.NameConflict));
        }

        folder.Name = newName;
        folder.ParentId = newParentId;
        folder.UpdatedAt = clock.UtcNow;

        await folders.UpdateAsync(folder, cancellationToken);

        logger.FolderUpdated(folder.Id, currentUser.Id, request.HasName, request.HasParentId);

        return Result.Success(UpdateFolderResponse.From(folder));
    }

    /// <summary>
    /// Whether putting <paramref name="folderId"/> under
    /// <paramref name="targetParentId"/> would make it its own ancestor: walks the
    /// parent chain upward from the target over one owner-scoped snapshot of the
    /// caller's active folders. The walk is bounded by the snapshot — every
    /// visited id is removed — so even pre-existing bad data could not loop it
    /// forever. The target itself being the folder is the first hop of the walk.
    /// </summary>
    private async Task<bool> WouldCreateCycleAsync(
        Guid folderId, Guid targetParentId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Folder> owned = await folders.ListActiveAsync(currentUser.Id, cancellationToken);
        Dictionary<Guid, Guid?> parentById = owned.ToDictionary(f => f.Id, f => f.ParentId);

        Guid? current = targetParentId;
        while (current is Guid currentId && parentById.Remove(currentId, out Guid? parent))
        {
            if (currentId == folderId)
            {
                return true;
            }

            current = parent;
        }

        return false;
    }
}

/// <summary>
/// Log messages for <see cref="UpdateFolderService"/>, co-located per the house
/// pattern: compile-time-generated and allocation-free via <c>[LoggerMessage]</c>.
/// Ids and flags only — never folder names (05-security.md). Information level:
/// mutations are rare and audit-worthy, unlike reads.
/// </summary>
internal static partial class UpdateFolderServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Folder {FolderId} updated by owner {OwnerId} (rename: {Renamed}, move: {Moved}).")]
    public static partial void FolderUpdated(
        this ILogger logger, Guid folderId, Guid ownerId, bool renamed, bool moved);
}
