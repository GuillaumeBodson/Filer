using System.Collections.Immutable;
using System.Reflection;

namespace Filer.Architecture.Tests;

/// <summary>
/// Discovers the Filer production assemblies that the boundary rules apply to.
/// </summary>
/// <remarks>
/// The test project references only the host (<c>Filer.Api</c>). The host is the
/// composition root, so the build copies the full transitive closure - every
/// module implementation, every <c>*.Contracts</c> project, and the kernels -
/// into this project's output. Scanning that output therefore picks up new
/// modules automatically as soon as they are wired into the host, with no
/// per-module reference to maintain here (10-solution-structure.md).
/// </remarks>
internal static class ProductionAssemblies
{
    internal const string SharedKernel = "Filer.SharedKernel";
    internal const string WebKernel = "Filer.WebKernel";
    internal const string Host = "Filer.Api";

    private const string Prefix = "Filer.";
    private const string ModulePrefix = "Filer.Modules.";
    private const string ContractsSuffix = ".Contracts";

    /// <summary>All Filer production assemblies (excludes test assemblies).</summary>
    internal static ImmutableArray<Assembly> All { get; } = Load();

    /// <summary>Module implementation assemblies (e.g. <c>Filer.Modules.Auth</c>), excluding their Contracts.</summary>
    internal static ImmutableArray<Assembly> ModuleImplementations { get; } =
        All.Where(IsModuleImplementation).ToImmutableArray();

    /// <summary>Module contract assemblies (e.g. <c>Filer.Modules.Auth.Contracts</c>).</summary>
    internal static ImmutableArray<Assembly> Contracts { get; } =
        All.Where(IsContracts).ToImmutableArray();

    internal static bool IsModuleImplementation(Assembly assembly) =>
        Name(assembly).StartsWith(ModulePrefix, StringComparison.Ordinal) && !IsContracts(assembly);

    internal static bool IsContracts(Assembly assembly) =>
        Name(assembly).EndsWith(ContractsSuffix, StringComparison.Ordinal);

    internal static string Name(Assembly assembly) => assembly.GetName().Name!;

    /// <summary>
    /// The module key shared by an implementation and its contracts, e.g. both
    /// <c>Filer.Modules.Auth</c> and <c>Filer.Modules.Auth.Contracts</c> map to <c>Auth</c>.
    /// </summary>
    internal static string ModuleKey(Assembly assembly)
    {
        var name = Name(assembly);
        if (!name.StartsWith(ModulePrefix, StringComparison.Ordinal))
        {
            return name;
        }

        var rest = name[ModulePrefix.Length..];
        if (rest.EndsWith(ContractsSuffix, StringComparison.Ordinal))
        {
            rest = rest[..^ContractsSuffix.Length];
        }

        return rest;
    }

    private static ImmutableArray<Assembly> Load()
    {
        var directory = AppContext.BaseDirectory;
        var assemblies = new List<Assembly>();

        foreach (var path in Directory.EnumerateFiles(directory, "Filer.*.dll"))
        {
            var fileName = Path.GetFileNameWithoutExtension(path);

            // Skip test assemblies (this one and any others that happen to be present).
            if (fileName.EndsWith(".Tests", StringComparison.Ordinal))
            {
                continue;
            }

            if (!fileName.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            assemblies.Add(Assembly.LoadFrom(path));
        }

        return assemblies
            .DistinctBy(Name)
            .OrderBy(Name, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
