using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Domain;
using Filer.Modules.Folders.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Folders.Features.Get;

/// <summary>
/// The get-folder slice (03-api-specification.md): resolve the caller's folder
/// and map it to the response DTO. Cross-owner, missing, and soft-deleted
/// folders are indistinguishable to the caller — all 404, never 403, so folder
/// ids cannot be probed (05-security.md). Same shape as Documents' get-metadata.
/// </summary>
public sealed class GetFolderService(
    IFolderStore folders,
    ICurrentUser currentUser,
    ILogger<GetFolderService> logger)
{
    public async Task<Result<GetFolderResponse>> HandleAsync(
        Guid folderId, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership filter below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<GetFolderResponse>(Error.Unauthorized());
        }

        // One owner-scoped, soft-delete-aware lookup: anything it does not return
        // is a uniform 404 (05-security.md).
        Folder? folder = await folders.FindActiveByIdAsync(
            currentUser.Id, folderId, cancellationToken);
        if (folder is null)
        {
            return Result.Failure<GetFolderResponse>(
                Error.NotFound("The folder was not found.", FoldersErrorCodes.FolderNotFound));
        }

        logger.FolderServed(folder.Id, currentUser.Id);

        return Result.Success(GetFolderResponse.From(folder));
    }
}

/// <summary>
/// Log messages for <see cref="GetFolderService"/>, co-located per the house
/// pattern: compile-time-generated and allocation-free via <c>[LoggerMessage]</c>.
/// Ids only — never folder names (05-security.md). Debug level: single-folder
/// reads are routine and high-frequency, like the list.
/// </summary>
internal static partial class GetFolderServiceLog
{
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Folder {FolderId} served to owner {OwnerId}.")]
    public static partial void FolderServed(this ILogger logger, Guid folderId, Guid ownerId);
}
