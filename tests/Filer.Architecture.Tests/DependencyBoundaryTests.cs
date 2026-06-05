using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Filer.Architecture.Tests;

/// <summary>
/// Enforces the dependency rules from 10-solution-structure.md ("Dependency Rules",
/// "Boundary Enforcement") at the granularity they are actually written in - the
/// project/assembly reference graph. A boundary that is only documented erodes;
/// encoding it as a failing test keeps project-per-module honest as the codebase grows.
/// </summary>
public sealed class DependencyBoundaryTests
{
    /// <summary>
    /// Acceptance criterion: no <c>Modules.X</c> references <c>Modules.Y</c> - only <c>*.Contracts</c>.
    /// A module's implementation may consume another module exclusively through that
    /// module's contracts assembly, never its implementation (rule 1).
    /// </summary>
    [Fact]
    public void Module_implementations_should_not_reference_another_modules_implementation()
    {
        var violations = new List<string>();

        foreach (var module in ProductionAssemblies.ModuleImplementations)
        {
            var self = ProductionAssemblies.Name(module);

            var offending = SolutionReferences(module)
                .Where(reference =>
                    reference.StartsWith("Filer.Modules.", StringComparison.Ordinal)
                    && !reference.EndsWith(".Contracts", StringComparison.Ordinal)
                    && !string.Equals(reference, self, StringComparison.Ordinal));

            violations.AddRange(offending.Select(reference => $"{self} -> {reference}"));
        }

        violations.Should().BeEmpty(
            "a module may depend only on another module's *.Contracts, never its implementation " +
            "(10-solution-structure.md, rule 1)");
    }

    /// <summary>
    /// Acceptance criterion: no <c>*.Contracts</c> references EF Core or another module.
    /// A contracts assembly references <c>SharedKernel</c> only - no EF Core, no
    /// infrastructure, no other module, no web kernel (rule 2; rule 6 keeps contracts web-free).
    /// </summary>
    [Fact]
    public void Contracts_should_reference_only_SharedKernel_and_never_EfCore_or_another_module()
    {
        var violations = new List<string>();

        foreach (var contracts in ProductionAssemblies.Contracts)
        {
            var self = ProductionAssemblies.Name(contracts);
            var references = contracts.GetReferencedAssemblies()
                .Select(name => name.Name!)
                .ToArray();

            var forbiddenSolutionRefs = references
                .Where(reference => reference.StartsWith("Filer.", StringComparison.Ordinal))
                .Where(reference => !string.Equals(reference, ProductionAssemblies.SharedKernel, StringComparison.Ordinal))
                .Where(reference => !string.Equals(reference, self, StringComparison.Ordinal));

            var efCoreRefs = references.Where(IsEntityFrameworkAssembly);

            violations.AddRange(forbiddenSolutionRefs.Select(reference => $"{self} -> {reference}"));
            violations.AddRange(efCoreRefs.Select(reference => $"{self} -> {reference} (EF Core)"));
        }

        violations.Should().BeEmpty(
            "a *.Contracts project references SharedKernel only - never EF Core, another module, or WebKernel " +
            "(10-solution-structure.md, rules 2 and 6)");
    }

    /// <summary>
    /// Acceptance criterion: nothing references <c>Filer.Api</c>.
    /// The host depends inward; it is referenced by no one (rule 3).
    /// </summary>
    [Fact]
    public void Nothing_should_reference_the_host()
    {
        var violations = ProductionAssemblies.All
            .Where(assembly => !string.Equals(ProductionAssemblies.Name(assembly), ProductionAssemblies.Host, StringComparison.Ordinal))
            .Where(assembly => SolutionReferences(assembly).Contains(ProductionAssemblies.Host))
            .Select(assembly => $"{ProductionAssemblies.Name(assembly)} -> {ProductionAssemblies.Host}")
            .ToList();

        violations.Should().BeEmpty(
            "the host is the composition root - nothing references Filer.Api (10-solution-structure.md, rule 3)");
    }

    /// <summary>
    /// <c>SharedKernel</c> is the bottom of the graph: it depends on nothing else in the solution (rule 4).
    /// </summary>
    [Fact]
    public void SharedKernel_should_not_reference_any_other_solution_assembly()
    {
        var sharedKernel = Single(ProductionAssemblies.SharedKernel);

        SolutionReferences(sharedKernel)
            .Should().BeEmpty("SharedKernel is the bottom of the dependency graph (10-solution-structure.md, rule 4)");
    }

    /// <summary>
    /// <c>WebKernel</c> is web-only and module-agnostic: among solution assemblies it
    /// references <c>SharedKernel</c> only, never a module or a <c>*.Contracts</c> project (rule 6, ADR-006).
    /// </summary>
    [Fact]
    public void WebKernel_should_reference_only_SharedKernel_among_solution_assemblies()
    {
        var webKernel = Single(ProductionAssemblies.WebKernel);

        SolutionReferences(webKernel)
            .Should().BeEquivalentTo(
                [ProductionAssemblies.SharedKernel],
                "WebKernel references SharedKernel only among solution assemblies (10-solution-structure.md, rule 6, ADR-006)");
    }

    private static Assembly Single(string name) =>
        ProductionAssemblies.All.Single(assembly =>
            string.Equals(ProductionAssemblies.Name(assembly), name, StringComparison.Ordinal));

    /// <summary>References to other Filer solution assemblies (self excluded).</summary>
    private static string[] SolutionReferences(Assembly assembly)
    {
        var self = ProductionAssemblies.Name(assembly);
        return assembly.GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => name.StartsWith("Filer.", StringComparison.Ordinal))
            .Where(name => !string.Equals(name, self, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsEntityFrameworkAssembly(string name) =>
        name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
        || name.StartsWith("Npgsql.EntityFrameworkCore", StringComparison.Ordinal);
}
