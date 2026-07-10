using Filer.Modules.AiAnalysis.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Filer.Modules.AiAnalysis.Tests;

/// <summary>
/// Registration tests for <see cref="AiAnalysisModule"/> (#51): the provider is
/// selected by configuration with a safe zero-footprint default, and an unknown
/// name fails fast at composition time rather than at first use (06).
/// </summary>
public sealed class AiAnalysisModuleTests
{
    [Fact]
    public void AddAiAnalysisModule_defaults_to_the_fake_provider_when_the_section_is_absent()
    {
        using ServiceProvider services = Build([]);

        services.GetRequiredService<IAIAnalysisProvider>().Should().BeOfType<FakeAnalysisProvider>();
    }

    [Fact]
    public void AddAiAnalysisModule_registers_the_fake_provider_when_configured()
    {
        using ServiceProvider services = Build(new Dictionary<string, string?>
        {
            ["AiAnalysis:Provider"] = AiAnalysisOptions.FakeProviderName,
        });

        services.GetRequiredService<IAIAnalysisProvider>().Should().BeOfType<FakeAnalysisProvider>();
    }

    [Fact]
    public void AddAiAnalysisModule_throws_for_an_unknown_provider()
    {
        Action act = () => Build(new Dictionary<string, string?>
        {
            ["AiAnalysis:Provider"] = "Skynet",
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Skynet*", "an unsupported provider must fail composition, not first use");
    }

    [Fact]
    public void AddAiAnalysisModule_resolves_the_ollama_provider_when_configured()
    {
        using ServiceProvider services = Build(new Dictionary<string, string?>
        {
            ["AiAnalysis:Provider"] = AiAnalysisOptions.OllamaProviderName,
        });

        // Resolved through the typed-client factory; no network is touched until a call.
        services.GetRequiredService<IAIAnalysisProvider>().Should().BeOfType<OllamaAnalysisProvider>();
    }

    [Fact]
    public void AddAiAnalysisModule_fails_validation_for_invalid_ollama_options()
    {
        using ServiceProvider services = Build(new Dictionary<string, string?>
        {
            ["AiAnalysis:Provider"] = AiAnalysisOptions.OllamaProviderName,
            ["AiAnalysis:Ollama:BaseUrl"] = "not-a-url",
            ["AiAnalysis:Ollama:TimeoutSeconds"] = "0",
        });

        Action act = () => services.GetRequiredService<IAIAnalysisProvider>();

        act.Should().Throw<OptionsValidationException>(
            "a misconfigured local provider must fail validation, not at first inference");
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddAiAnalysisModule(configuration);

        return services.BuildServiceProvider();
    }
}
