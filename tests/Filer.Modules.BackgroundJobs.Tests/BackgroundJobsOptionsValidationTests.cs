using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Filer.Modules.BackgroundJobs.Tests;

/// <summary>
/// Configuration guards on <see cref="BackgroundJobsOptions"/> (#281). The poll
/// interval is the dominant term in end-to-end analysis latency — nothing signals
/// the worker that a job arrived — so it is a value that gets tuned, and a tuned
/// value needs a floor: each poll is a claim query against PostgreSQL.
/// </summary>
public sealed class BackgroundJobsOptionsValidationTests
{
    [Fact]
    public void PollInterval_WhenUnconfigured_DefaultsToOneSecond()
    {
        BackgroundJobsOptions options = Resolve([]);

        // Half of this is the average wait a user pays on top of inference, so
        // the default is a latency decision, not an arbitrary tick (#281).
        options.PollInterval.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void PollInterval_WhenBelowTheFloor_FailsValidation()
    {
        Action act = () => Resolve(new Dictionary<string, string?>
        {
            ["BackgroundJobs:PollInterval"] = "00:00:00.001",
        });

        act.Should().Throw<OptionsValidationException>(
                "a mistyped interval must fail at composition, not spin the database in production")
            .WithMessage("*100*");
    }

    [Fact]
    public void PollInterval_AtTheFloor_IsAccepted()
    {
        BackgroundJobsOptions options = Resolve(new Dictionary<string, string?>
        {
            ["BackgroundJobs:PollInterval"] = "00:00:00.100",
        });

        options.PollInterval.Should().Be(BackgroundJobsOptions.MinPollInterval);
    }

    /// <summary>
    /// Registers the module and reads the options back, which is what triggers
    /// the <c>Validate</c> rules — the same path the host takes at startup.
    /// </summary>
    private static BackgroundJobsOptions Resolve(Dictionary<string, string?> settings)
    {
        // The module owns a DbContext; registering it needs a connection string,
        // but nothing connects until a query runs.
        settings["ConnectionStrings:Postgres"] = "Host=localhost;Database=filer;Username=filer;Password=filer";

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddBackgroundJobsModule(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<BackgroundJobsOptions>>().Value;
    }
}
