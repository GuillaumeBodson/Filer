using Filer.SharedKernel.Authorization;

namespace Filer.Modules.Tags.Tests.TestSupport;

/// <summary>A caller with a fixed identity — or an anonymous one.</summary>
internal sealed class StubCurrentUser(bool isAuthenticated, Guid id) : ICurrentUser
{
    public static StubCurrentUser Anonymous { get; } = new(false, Guid.Empty);

    public bool IsAuthenticated { get; } = isAuthenticated;

    public Guid Id { get; } = id;

    public Guid? TenantId => null;
}
