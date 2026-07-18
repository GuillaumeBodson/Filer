using Filer.Modules.BackgroundJobs.Contracts;
using Filer.Modules.BackgroundJobs.Messaging;
using Filer.Modules.BackgroundJobs.Persistence;
using Filer.Modules.BackgroundJobs.Queueing;
using Filer.Modules.BackgroundJobs.Worker;
using Filer.SharedKernel.Configuration;
using Filer.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
                options => options.SweepInterval > TimeSpan.Zero,
                "BackgroundJobs:SweepInterval must be positive.")
            .Validate(
                options => options.MaxAttempts >= 1,
                "BackgroundJobs:MaxAttempts must be at least 1.")
            .Validate(
                options => options.RetryBaseDelay > TimeSpan.Zero,
                "BackgroundJobs:RetryBaseDelay must be positive.")
            .Validate(
                options => options.QueueDepthReportInterval > TimeSpan.Zero,
                "BackgroundJobs:QueueDepthReportInterval must be positive.")
            .Validate(
                options => options.Queue != QueueDispatch.RabbitMq
                    || (!string.IsNullOrWhiteSpace(options.RabbitMq.HostName)
                        && options.RabbitMq.Port is >= 1 and <= 65535
                        && !string.IsNullOrWhiteSpace(options.RabbitMq.QueueName)),
                "BackgroundJobs:RabbitMq requires a host name, a valid port and a queue name when Queue is RabbitMq.")
            .ValidateOnStart();

        // The module owns its data in the 'jobs' Postgres schema.
        string connectionString = configuration.GetConnectionString(ConnectionStringNames.Postgres)
            ?? throw new InvalidOperationException($"The '{ConnectionStringNames.Postgres}' connection string is missing.");

        services.AddDbContext<JobsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", JobsDbContext.Schema)));

        // Dispatch mechanism selected by configuration, mirroring the storage
        // provider pattern (07): eager read because the registration shape is
        // decided while the builder is still composing.
        BackgroundJobsOptions dispatchOptions =
            configuration.GetSection(BackgroundJobsOptions.SectionName).Get<BackgroundJobsOptions>() ?? new();

        // The durable queue other modules enqueue into, and the worker's seams.
        services.AddScoped<EfBackgroundJobQueue>();
        if (dispatchOptions.Queue == QueueDispatch.RabbitMq)
        {
            // Outbox relay (ADR-008): same insert+commit, then a broker wake-up.
            // The consumer runs the identical claim; the polling worker degrades
            // to the sweeper. Broker readiness is a module-owned check (#159
            // pattern) reporting Degraded, because broker-down still processes.
            services.AddSingleton<RabbitMqConnection>();
            services.AddSingleton<IAnalysisJobDispatcher, RabbitMqJobPublisher>();
            services.AddScoped<IBackgroundJobQueue>(provider => new RabbitMqBackgroundJobQueue(
                provider.GetRequiredService<EfBackgroundJobQueue>(),
                provider.GetRequiredService<IAnalysisJobDispatcher>(),
                provider.GetRequiredService<ILogger<RabbitMqBackgroundJobQueue>>()));
            services.AddHostedService<RabbitMqJobConsumer>();
            services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);
        }
        else
        {
            services.AddScoped<IBackgroundJobQueue>(provider =>
                provider.GetRequiredService<EfBackgroundJobQueue>());
        }

        services.AddScoped<IAnalysisJobStore, EfAnalysisJobStore>();

        // Public read surface for the Documents analysis slices (#54/#55): latest
        // job status + result, never the error text. The implementation stays
        // internal behind the Contracts interface.
        services.AddScoped<IAnalysisJobReader, AnalysisJobReader>();

        // Fallback handler: the AI Analysis module registers the real one before
        // this module runs (Program.cs ordering), so TryAdd keeps the swap additive
        // and the no-op only wins in hosts without AI analysis (06).
        services.TryAddScoped<IAnalysisJobHandler, NoOpAnalysisJobHandler>();

        // Observability (04-non-functional.md): meter-backed counters/histogram/
        // gauge. AddMetrics is idempotent and supplies IMeterFactory in bare hosts.
        services.AddMetrics();
        services.AddSingleton<BackgroundJobsMetrics>();

        // The claim-and-process core shared by the polling worker and the RabbitMQ
        // consumer (ADR-008); the worker always runs — as the primary consumer
        // under Db dispatch, as the sweeper fallback under RabbitMq.
        services.AddSingleton<AnalysisJobProcessor>();
        services.AddHostedService<AnalysisJobWorker>();

        return services;
    }
}
