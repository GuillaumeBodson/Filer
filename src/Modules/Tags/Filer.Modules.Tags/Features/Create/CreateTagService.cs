using Filer.Modules.Tags.Contracts;
using Filer.Modules.Tags.Domain;
using Filer.Modules.Tags.Persistence;
using Filer.SharedKernel.Authorization;
using Filer.SharedKernel.Results;
using Filer.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace Filer.Modules.Tags.Features.Create;

/// <summary>
/// The create-tag slice (03-api-specification.md): validate the request, enforce
/// per-owner name uniqueness, persist. The name check is the business 409; the
/// unique index on (OwnerId, Name) is the race-condition backstop
/// (02-data-model.md). Tags are flat and per-owner, so unlike the Folders create
/// slice there is no parent to verify.
/// </summary>
public sealed class CreateTagService(
    ITagStore tags,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<CreateTagService> logger)
{
    public async Task<Result<CreateTagResponse>> HandleAsync(
        CreateTagRequest request, CancellationToken cancellationToken)
    {
        // Defense in depth: the endpoint already requires authorization, but the
        // owner-scoped checks below must never run with an unauthenticated principal.
        if (!currentUser.IsAuthenticated)
        {
            return Result.Failure<CreateTagResponse>(Error.Unauthorized());
        }

        Result validation = CreateTagValidator.Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<CreateTagResponse>(validation.Error!);
        }

        // The validator guarantees a non-empty trimmed name; persist and compare
        // the trimmed form so "  urgent " and "urgent" are the same tag.
        string name = request.Name!.Trim();

        if (await tags.NameExistsAsync(currentUser.Id, name, cancellationToken))
        {
            return Result.Failure<CreateTagResponse>(Error.Conflict(
                "A tag with the same name already exists.",
                TagsErrorCodes.NameConflict));
        }

        DateTimeOffset now = clock.UtcNow;
        var tag = new Tag
        {
            OwnerId = currentUser.Id,
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await tags.AddAsync(tag, cancellationToken);

        logger.TagCreated(tag.Id, currentUser.Id);

        return Result.Success(CreateTagResponse.From(tag));
    }
}

/// <summary>
/// Log messages for <see cref="CreateTagService"/>, co-located per the house
/// pattern: compile-time-generated and allocation-free via <c>[LoggerMessage]</c>.
/// Ids only — never tag names (05-security.md). Information level: mutations are
/// rare and audit-worthy, unlike reads.
/// </summary>
internal static partial class CreateTagServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Tag {TagId} created by owner {OwnerId}.")]
    public static partial void TagCreated(this ILogger logger, Guid tagId, Guid ownerId);
}
