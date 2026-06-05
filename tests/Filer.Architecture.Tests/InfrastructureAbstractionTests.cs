using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Filer.Architecture.Tests;

/// <summary>
/// Type-level boundary checks driven by NetArchTest.eNhancedEdition. Where
/// <see cref="DependencyBoundaryTests"/> works on the assembly-reference graph,
/// these inspect actual type usage - which is what "feature code talks to
/// infrastructure only through its abstraction" really means (rule 5, 06/07).
/// </summary>
public sealed class InfrastructureAbstractionTests
{
    // Infrastructure abstractions live in their module's *.Contracts (06/07/10).
    // Concrete providers (e.g. LocalFileSystemStorageProvider) live in the module
    // implementation and must not leak across module boundaries.
    private static readonly string[] AbstractionInterfaceNames =
    [
        "IFileStorageProvider",
        "IAIAnalysisProvider",
    ];

    /// <summary>
    /// Acceptance criterion: feature code references storage/AI only via their abstractions.
    /// No module implementation may take a type-level dependency on a concrete provider
    /// defined by another module; consumers depend on the interface from *.Contracts (rule 5).
    /// </summary>
    /// <remarks>
    /// Forward-looking: until the Storage / AI Analysis modules land, no concrete
    /// provider exists and the rule holds vacuously. It gains teeth automatically the
    /// moment such a provider is added - no edit to this test required.
    /// </remarks>
    [Fact]
    public void Modules_should_not_depend_on_another_modules_concrete_infrastructure_provider()
    {
        var providers = ConcreteProviderTypes();

        foreach (var module in ProductionAssemblies.ModuleImplementations)
        {
            var moduleKey = ProductionAssemblies.ModuleKey(module);

            var foreignProviders = providers
                .Where(provider => !string.Equals(ProductionAssemblies.ModuleKey(provider.Assembly), moduleKey, StringComparison.Ordinal))
                .Select(provider => provider.FullName!)
                .ToArray();

            if (foreignProviders.Length == 0)
            {
                continue;
            }

            Types.InAssembly(module)
                .ShouldNot()
                .HaveDependencyOnAny(foreignProviders)
                .GetResult()
                .ShouldBeSuccessful(
                    $"{ProductionAssemblies.Name(module)} must consume storage/AI only through their " +
                    "abstractions, never another module's concrete provider (10-solution-structure.md, rule 5; 06/07)");
        }
    }

    /// <summary>
    /// The infrastructure abstractions themselves must be declared in a <c>*.Contracts</c>
    /// assembly so other modules can depend on them without touching an implementation (rule 5).
    /// </summary>
    [Fact]
    public void Infrastructure_abstractions_should_be_declared_in_contracts()
    {
        var misplaced = ProductionAssemblies.All
            .Where(assembly => !ProductionAssemblies.IsContracts(assembly))
            .SelectMany(SafeGetTypes)
            .Where(type => type.IsInterface && AbstractionInterfaceNames.Contains(type.Name, StringComparer.Ordinal))
            .Select(type => $"{type.FullName} in {type.Assembly.GetName().Name}")
            .ToList();

        misplaced.Should().BeEmpty(
            "infrastructure abstractions (IFileStorageProvider, IAIAnalysisProvider) belong in a " +
            "*.Contracts project, not an implementation (10-solution-structure.md, rule 5; 06/07)");
    }

    /// <summary>
    /// Type-level reinforcement of rule 2: no contracts type uses an EF Core type.
    /// </summary>
    [Fact]
    public void Contracts_types_should_not_depend_on_EfCore()
    {
        if (ProductionAssemblies.Contracts.Length == 0)
        {
            return;
        }

        Types.InAssemblies(ProductionAssemblies.Contracts)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql.EntityFrameworkCore")
            .GetResult()
            .ShouldBeSuccessful("contracts carry no EF Core dependency (10-solution-structure.md, rule 2)");
    }

    /// <summary>
    /// Type-level reinforcement of rule 3: nothing outside the host uses a Filer.Api type.
    /// </summary>
    [Fact]
    public void No_type_outside_the_host_should_depend_on_the_host()
    {
        var nonHost = ProductionAssemblies.All
            .Where(assembly => !string.Equals(ProductionAssemblies.Name(assembly), ProductionAssemblies.Host, StringComparison.Ordinal))
            .ToArray();

        if (nonHost.Length == 0)
        {
            return;
        }

        Types.InAssemblies(nonHost)
            .ShouldNot()
            .HaveDependencyOnAny(ProductionAssemblies.Host)
            .GetResult()
            .ShouldBeSuccessful("nothing depends on the host (10-solution-structure.md, rule 3)");
    }

    private static Type[] ConcreteProviderTypes()
    {
        var interfaces = ProductionAssemblies.All
            .SelectMany(SafeGetTypes)
            .Where(type => type.IsInterface && AbstractionInterfaceNames.Contains(type.Name, StringComparer.Ordinal))
            .ToArray();

        if (interfaces.Length == 0)
        {
            return [];
        }

        return ProductionAssemblies.All
            .SelectMany(SafeGetTypes)
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => interfaces.Any(@interface => @interface.IsAssignableFrom(type)))
            .ToArray();
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
    }
}
