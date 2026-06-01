using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Filer.IntegrationTests.Infrastructure;

/// <summary>
/// Shares a single <see cref="FilerApiFactory"/> — and therefore one Postgres and
/// one host — across every integration test class. Starting a container per class
/// would dominate the runtime; tests stay isolated by using unique data
/// (see <see cref="TestData"/>) rather than a fresh database each time.
/// </summary>
[CollectionDefinition(Name)]
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The 'Collection' suffix is the xUnit [CollectionDefinition] idiom, " +
                    "not a System.Collections type; the name is intentional.")]
public sealed class IntegrationCollection : ICollectionFixture<FilerApiFactory>
{
    public const string Name = "integration";
}
