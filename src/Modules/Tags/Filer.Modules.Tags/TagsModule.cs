using Filer.Modules.Tags.Contracts;
using Filer.Modules.Tags.Features.Create;
using Filer.Modules.Tags.Features.Delete;
using Filer.Modules.Tags.Features.List;
using Filer.Modules.Tags.Features.Rename;
using Filer.Modules.Tags.Persistence;
using Filer.SharedKernel.Configuration;
using Filer.SharedKernel.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Filer.Modules.Tags;

/// <summary>
/// Registration entry point for the Tags module. The host invokes
/// <see cref="AddTagsModule"/> and <see cref="MapTagsEndpoints"/> only;
/// it never reaches into module internals (10-solution-structure.md).
/// </summary>
public static class TagsModule
{
    public static IServiceCollection AddTagsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Cross-cutting clock primitive; TryAdd so the host may also register it.
        services.TryAddSingleton<IClock, SystemClock>();

        // The module owns its data in the 'tags' Postgres schema.
        string connectionString = configuration.GetConnectionString(ConnectionStringNames.Postgres)
            ?? throw new InvalidOperationException($"The '{ConnectionStringNames.Postgres}' connection string is missing.");

        services.AddDbContext<TagsDbContext>(dbOptions =>
            dbOptions.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", TagsDbContext.Schema)));

        services.AddScoped<ITagStore, EfTagStore>();

        // Public surface for other modules (the Documents document-tag slices,
        // ADR-009): the implementation stays internal behind the Contracts
        // interface, mirroring Folders' IFolderOwnershipChecker.
        services.AddScoped<ITagOwnershipChecker, TagOwnershipChecker>();

        // Feature services (vertical slices).
        services.AddScoped<CreateTagService>();
        services.AddScoped<ListTagsService>();
        services.AddScoped<RenameTagService>();
        services.AddScoped<DeleteTagService>();

        return services;
    }

    public static IEndpointRouteBuilder MapTagsEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup(TagsRoutes.BasePath);

        group.MapListTags();
        group.MapCreateTag();
        group.MapRenameTag();
        group.MapDeleteTag();

        return routes;
    }
}
