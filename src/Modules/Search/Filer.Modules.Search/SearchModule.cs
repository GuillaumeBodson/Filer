using Filer.Modules.Search.Features.SearchDocuments;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Filer.Modules.Search;

/// <summary>
/// Registration entry point for the Search module. The host invokes
/// <see cref="AddSearchModule"/> and <see cref="MapSearchEndpoints"/> only; it
/// never reaches into module internals (10-solution-structure.md). The module
/// owns no data: it consumes the searchable rows through Documents' Contracts
/// (<c>IOwnerDocumentSearch</c>), so — like Storage — it has no DbContext and no
/// migrations.
/// </summary>
public static class SearchModule
{
    public static IServiceCollection AddSearchModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Feature services (vertical slices). No options today; the
        // configuration parameter keeps the host-facing signature uniform
        // across modules.
        _ = configuration;

        services.AddScoped<SearchDocumentsService>();

        return services;
    }

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup(SearchRoutes.BasePath);

        group.MapSearchDocuments();

        return routes;
    }
}
