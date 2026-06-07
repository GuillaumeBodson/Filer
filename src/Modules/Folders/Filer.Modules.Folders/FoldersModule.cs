using Filer.Modules.Folders.Contracts;
using Filer.Modules.Folders.Features.Create;
using Filer.Modules.Folders.Features.Get;
using Filer.Modules.Folders.Features.List;
using Filer.Modules.Folders.Persistence;
using Filer.SharedKernel.Configuration;
using Filer.SharedKernel.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Filer.Modules.Folders;

/// <summary>
/// Registration entry point for the Folders module. The host invokes
/// <see cref="AddFoldersModule"/> and <see cref="MapFoldersEndpoints"/> only;
/// it never reaches into module internals (10-solution-structure.md).
/// </summary>
public static class FoldersModule
{
    public static IServiceCollection AddFoldersModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Cross-cutting clock primitive; TryAdd so the host may also register it.
        services.TryAddSingleton<IClock, SystemClock>();

        // The module owns its data in the 'folders' Postgres schema.
        string connectionString = configuration.GetConnectionString(ConnectionStringNames.Postgres)
            ?? throw new InvalidOperationException($"The '{ConnectionStringNames.Postgres}' connection string is missing.");

        services.AddDbContext<FoldersDbContext>(dbOptions =>
            dbOptions.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", FoldersDbContext.Schema)));

        services.AddScoped<IFolderStore, EfFolderStore>();

        // Public surface for other modules (Documents' move-target check); the
        // implementation stays internal behind the Contracts interface.
        services.AddScoped<IFolderOwnershipChecker, FolderOwnershipChecker>();

        // Feature services (vertical slices).
        services.AddScoped<CreateFolderService>();
        services.AddScoped<ListFoldersService>();
        services.AddScoped<GetFolderService>();

        return services;
    }

    public static IEndpointRouteBuilder MapFoldersEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup(FoldersRoutes.BasePath);

        group.MapCreateFolder();
        group.MapListFolders();
        group.MapGetFolder();

        return routes;
    }
}
