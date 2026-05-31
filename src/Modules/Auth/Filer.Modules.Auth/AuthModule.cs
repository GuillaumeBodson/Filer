using System.Text;
using Filer.Modules.Auth.Authentication;
using Filer.Modules.Auth.Domain;
using Filer.Modules.Auth.Features.Login;
using Filer.Modules.Auth.Features.Me;
using Filer.Modules.Auth.Features.Register;
using Filer.Modules.Auth.Persistence;
using Filer.SharedKernel.Configuration;
using Filer.SharedKernel.Time;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Filer.Modules.Auth;

/// <summary>
/// Registration entry point for the Auth module. The host invokes
/// <see cref="AddAuthModule"/> and <see cref="MapAuthEndpoints"/> only; it never
/// reaches into module internals (10-solution-structure.md).
/// </summary>
public static class AuthModule
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Cross-cutting clock primitive; TryAdd so the host may also register it.
        services.TryAddSingleton<IClock, SystemClock>();

        // JWT options, validated on startup.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("The 'Jwt' configuration section is missing.");

        // The module owns its data in the 'auth' Postgres schema.
        string connectionString = configuration.GetConnectionString(ConnectionStringNames.Postgres)
            ?? throw new InvalidOperationException($"The '{ConnectionStringNames.Postgres}' connection string is missing.");

        services.AddDbContext<AuthDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AuthDbContext.Schema)));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AuthDbContext>();

        // Token issuing + bearer validation, keyed off the same signing material.
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    NameClaimType = Contracts.AuthClaimTypes.Subject,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        // Feature services (vertical slices).
        services.AddScoped<RegisterService>();
        services.AddScoped<LoginService>();

        return services;
    }

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/api/v1/auth");

        group.MapRegister();
        group.MapLogin();
        group.MapMe();

        return routes;
    }
}
