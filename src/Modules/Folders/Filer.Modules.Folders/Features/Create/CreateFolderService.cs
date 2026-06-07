using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Domain;
using Filer.Modules.Folders.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Folders.Features.Create;

/// <summary>
/// The create-folder slice (03-api-specification.md): validate the request,
/// verify the optional parent, enforce sibling-name uniqueness, persist. A parent
/// the caller does not own and a missing parent are a uniform 404
/// (05-security.md) — folder ids must not be probeable. The sibling check is the
/// business 409; the partial unique index on (OwnerId, ParentId, Name) is the
/// race-condition backstop (02-data-model.md).
/// </summary>
public sealed class CreateFolderService(
    IFolderStore folders,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<CreateFolderService> logger)
{
    public async Task<Result<CreateFolderResponse>> HandleAsync(
        CreateFolderRequest request, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // ownership checks below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<CreateFolderResponse>(Error.Unauthorized());
        }

        Result validation = CreateFolderValidator.Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<CreateFolderResponse>(validation.Error!);
        }

        // The validator guarantees a non-empty trimmed name; persist and compare
        // the trimmed form so "  Inbox " and "Inbox" are the same folder.
        string name = request.Name!.Trim();

        // A non-null parent must be an active folder the caller owns; null is the
        // top level and needs no check (02-data-model.md). Owner-scoped lookup, so
        // cross-owner and missing parents are indistinguishable (05-security.md).
        if (request.ParentId is Guid parentId
            && !await folders.ActiveExistsAsync(currentUser.Id, parentId, cancellationToken))
        {
            return Result.Failure<CreateFolderResponse>(
                Error.NotFound("The parent folder was not found.", FoldersErrorCodes.ParentNotFound));
        }

        if (await folders.ActiveSiblingNameExistsAsync(
                currentUser.Id, request.ParentId, name, cancellationToken))
        {
            return Result.Failure<CreateFolderResponse>(Error.Conflict(
                "A folder with the same name already exists at this level.",
                FoldersErrorCodes.NameConflict));
        }

        DateTimeOffset now = clock.UtcNow;
        var folder = new Folder
        {
            OwnerId = currentUser.Id,
            ParentId = request.ParentId,
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await folders.AddAsync(folder, cancellationToken);

        logger.FolderCreated(folder.Id, currentUser.Id, folder.ParentId is not null);

        return Result.Success(CreateFolderResponse.From(folder));
    }
}

/// <summary>
/// Log messages for <see cref="CreateFolderService"/>, co-located per the house
/// pattern: compile-time-generated and allocation-free via <c>[LoggerMessage]</c>.
/// Ids and flags only — never folder names (05-security.md). Information level:
/// mutations are rare and audit-worthy, unlike reads.
/// </summary>
internal static partial class CreateFolderServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Folder {FolderId} created by owner {OwnerId} (nested: {Nested}).")]
    public static partial void FolderCreated(
        this ILogger logger, Guid folderId, Guid ownerId, bool nested);
}
