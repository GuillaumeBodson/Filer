using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.BackgroundJobs.Persistence;
using Filer.Modules.BackgroundJobs.Queueing;
using Filer.Modules.BackgroundJobs.Worker;
using Filer.SharedKernel.Configuration;
using Filer.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Filer.Modules.BackgroundJobs;

/// <summary>
/// Registration entry point for the BackgroundJobs module. The host invokes
/// <see cref="AddBackgroundJobsModule"/> only; it never reaches into module
/// internals (10-solution-structure.md). The module maps no endpoints: other
/// modules reach it through <see cref="IBackgroundJobQueue"/> and the worker
/// runs as a hosted service.
/// </summary>
public static class BackgroundJobsModule
{
    public static IServiceCollection AddBackgroundJobsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Cross-cutting clock primitive; TryAdd so the host may also register it.
        services.TryAddSingleton<IClock, SystemClock>();

        // The section is optional — every option has a safe default.
        services.AddOptions<BackgroundJobsOptions>()
            .Bind(configuration.GetSection(BackgroundJobsOptions.SectionName))
            .Validate(
                options => options.PollInterval > TimeSpan.Zero,
                "BackgroundJobs:PollInterval must be positive.")
            .Validate(
                options => options.MaxAttempts >= 1,
                "BackgroundJobs:MaxAttempts must be at least 1.")
            .Validate(
                options => options.RetryBaseDelay > TimeSpan.Zero,
                "BackgroundJobs:RetryBaseDelay must be positive.")
            .Validate(
                options => options.QueueDepthReportInterval > TimeSpan.Zero,
                "BackgroundJobs:QueueDepthReportInterval must be positive.")
            .ValidateOnStart();

        // The module owns its data in the 'jobs' Postgres schema.
        string connectionString = configuration.GetConnectionString(ConnectionStringNames.Postgres)
            ?? throw new InvalidOperationException($"The '{ConnectionStringNames.Postgres}' connection string is missing.");

        services.AddDbContext<JobsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", JobsDbContext.Schema)));

        // The durable queue other modules enqueue into, and the worker's seams.
        services.AddScoped<IBackgroundJobQueue, EfBackgroundJobQueue>();
        services.AddScoped<IAnalysisJobStore, EfAnalysisJobStore>();

        // Public read surface for the Documents analysis slices (#54/#55): latest
        // job status + result, never the error text. The implementation stays
        // internal behind the Contracts interface.
        services.AddScoped<IAnalysisJobReader, EfAnalysisJobReader>();

        // Fallback handler: the AI Analysis module registers the real one before
        // this module runs (Program.cs ordering), so TryAdd keeps the swap additive
        // and the no-op only wins in hosts without AI analysis (06).
        services.TryAddScoped<IAnalysisJobHandler, NoOpAnalysisJobHandler>();

        // Observability (04-non-functional.md): meter-backed counters/histogram/
        // gauge. AddMetrics is idempotent and supplies IMeterFactory in bare hosts.
        services.AddMetrics();
        services.AddSingleton<BackgroundJobsMetrics>();

        services.AddHostedService<AnalysisJobWorker>();

        return services;
    }
}
