using Filer.Modules.Tags.Contracts;
using Filer.Modules.Tags.Domain;
using Filer.Modules.Tags.Persistence;
using FluentAssertions;
using Moq;
using Xunit;

namespace Filer.Modules.Tags.Tests.Persistence;

/// <summary>
/// The cross-module name-resolution adapter behind <see cref="ITagNameResolver"/>
/// (#55): trimmed, case-insensitive, deterministic when several owned tags match,
/// and silent about unresolved names — the caller decides what absence means.
/// </summary>
public sealed class TagNameResolverTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly Mock<ITagStore> _store = new(MockBehavior.Strict);

    private TagNameResolver CreateSut() => new(_store.Object);

    private void ArrangeOwnedTags(params Tag[] tags) =>
        _store.Setup(s => s.ListAsync(OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tags);

    private static Tag OwnedTag(Guid id, string name) =>
        new() { Id = id, OwnerId = OwnerId, Name = name };

    [Fact]
    public async Task ResolveOwnedByNamesAsync_WithNoUsableNames_ReturnsEmpty_WithoutQuerying()
    {
        IReadOnlyList<ResolvedTag> resolved = await CreateSut()
            .ResolveOwnedByNamesAsync(OwnerId, ["  ", string.Empty], CancellationToken.None);

        resolved.Should().BeEmpty();
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveOwnedByNamesAsync_MatchesTrimmedAndCaseInsensitive()
    {
        Guid invoicesId = Guid.NewGuid();
        ArrangeOwnedTags(OwnedTag(invoicesId, "Invoices"));

        IReadOnlyList<ResolvedTag> resolved = await CreateSut()
            .ResolveOwnedByNamesAsync(OwnerId, ["  inVOICES "], CancellationToken.None);

        resolved.Should().Equal(new ResolvedTag(invoicesId, "Invoices"));
    }

    [Fact]
    public async Task ResolveOwnedByNamesAsync_PrefersExactCaseMatch()
    {
        // Per-owner uniqueness is case-sensitive (02-data-model.md), so "invoices"
        // and "Invoices" can coexist; the exact-case tag must win.
        Guid lowerId = Guid.NewGuid();
        Guid upperId = Guid.NewGuid();
        ArrangeOwnedTags(OwnedTag(upperId, "Invoices"), OwnedTag(lowerId, "invoices"));

        IReadOnlyList<ResolvedTag> resolved = await CreateSut()
            .ResolveOwnedByNamesAsync(OwnerId, ["invoices"], CancellationToken.None);

        resolved.Should().Equal(new ResolvedTag(lowerId, "invoices"));
    }

    [Fact]
    public async Task ResolveOwnedByNamesAsync_OmitsUnresolvedNames()
    {
        Guid invoicesId = Guid.NewGuid();
        ArrangeOwnedTags(OwnedTag(invoicesId, "invoices"));

        IReadOnlyList<ResolvedTag> resolved = await CreateSut()
            .ResolveOwnedByNamesAsync(OwnerId, ["invoices", "missing"], CancellationToken.None);

        resolved.Should().Equal(new ResolvedTag(invoicesId, "invoices"));
    }

    [Fact]
    public async Task ResolveOwnedByNamesAsync_DeduplicatesNamesCaseInsensitively()
    {
        Guid invoicesId = Guid.NewGuid();
        ArrangeOwnedTags(OwnedTag(invoicesId, "invoices"));

        IReadOnlyList<ResolvedTag> resolved = await CreateSut()
            .ResolveOwnedByNamesAsync(OwnerId, ["invoices", "INVOICES"], CancellationToken.None);

        resolved.Should().Equal(new ResolvedTag(invoicesId, "invoices"));
    }
}
