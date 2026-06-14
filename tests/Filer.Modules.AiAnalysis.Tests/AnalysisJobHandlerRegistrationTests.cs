using Filer.Modules.BackgroundJobs.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Filer.Modules.AiAnalysis.Tests;

/// <summary>
/// Registration tests for the analysis job handler (#53): the module supplies the
/// real <see cref="IAnalysisJobHandler"/> unconditionally, so the BackgroundJobs
/// module's <c>TryAddScoped</c> no-op fallback never wins in a host that registers
/// AI analysis first (Program.cs ordering).
/// </summary>
public sealed class AnalysisJobHandlerRegistrationTests
{
    [Fact]
    public void AddAiAnalysisModule_registers_the_real_analysis_job_handler()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        ServiceCollection services = new();
        services.AddLogging();

        services.AddAiAnalysisModule(configuration);

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IAnalysisJobHandler)
            && descriptor.ImplementationType == typeof(AnalysisJobHandler)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }
}
