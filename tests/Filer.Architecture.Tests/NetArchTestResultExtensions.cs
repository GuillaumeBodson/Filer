using FluentAssertions;
using NetArchTest.Rules;

namespace Filer.Architecture.Tests;

/// <summary>
/// Bridges a NetArchTest <see cref="TestResult"/> to a FluentAssertions failure that
/// names the offending types (and NetArchTest's per-type explanation) instead of a
/// bare "expected true but found false".
/// </summary>
internal static class NetArchTestResultExtensions
{
    internal static void ShouldBeSuccessful(this TestResult result, string because)
    {
        var failures = result.FailingTypes is null
            ? string.Empty
            : string.Join(
                Environment.NewLine,
                result.FailingTypes.Select(type => $"  - {type.FullName}: {type.Explanation}"));

        result.IsSuccessful.Should().BeTrue(
            "{0}. Offending types:{1}{2}", because, Environment.NewLine, failures);
    }
}
