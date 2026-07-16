using Filer.Modules.AiAnalysis.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
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
    public void AddAiAnalysisModule_WhenSectionAbsent_DefaultsToFakeProvider()
    {
        using ServiceProvider services = Build([]);

        services.GetRequiredService<IAIAnalysisProvider>().Should().BeOfType<FakeAnalysisProvider>();
    }

    [Fact]
    public void AddAiAnalysisModule_WhenFakeConfigured_RegistersFakeProvider()
    {
        using ServiceProvider services = Build(new Dictionary<string, string?>
        {
            ["AiAnalysis:Provider"] = AiAnalysisOptions.FakeProviderName,
        });

        services.GetRequiredService<IAIAnalysisProvider>().Should().BeOfType<FakeAnalysisProvider>();
    }

    [Fact]
    public void AddAiAnalysisModule_WhenProviderUnknown_Throws()
    {
        Action act = () => Build(new Dictionary<string, string?>
        {
            ["AiAnalysis:Provider"] = "Skynet",
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Skynet*", "an unsupported provider must fail composition, not first use");
    }

    [Fact]
    public void AddAiAnalysisModule_WhenOllamaConfigured_ResolvesOllamaProvider()
    {
        using ServiceProvider services = Build(new Dictionary<string, string?>
        {
            ["AiAnalysis:Provider"] = AiAnalysisOptions.OllamaProviderName,
        });

        // Resolved through the typed-client factory; no network is touched until a call.
        services.GetRequiredService<IAIAnalysisProvider>().Should().BeOfType<OllamaAnalysisProvider>();
    }

    [Fact]
    public void AddAiAnalysisModule_WhenAgenticOptedIn_ResolvesAgenticProvider()
    {
        // #119: the experimental two-pass variant is config-selected and never the
        // default — the plain adapter and the fake stay exactly as they were.
        using ServiceProvider services = Build(
            new Dictionary<string, string?>
            {
                ["AiAnalysis:Provider"] = AiAnalysisOptions.OllamaAgenticProviderName,
            },
            // The agentic provider samples folder contents through the Documents
            // module's port; the host registers it, so the test stubs it.
            extra => extra.AddSingleton(Mock.Of<Filer.Modules.Documents.Contracts.IFolderContentLookup>()));

        services.GetRequiredService<IAIAnalysisProvider>().Should().BeOfType<OllamaAgenticAnalysisProvider>();
    }

    [Fact]
    public void AddAiAnalysisModule_WhenOllamaOptionsInvalid_FailsValidation()
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

    private static ServiceProvider Build(
        Dictionary<string, string?> settings, Action<ServiceCollection>? extraRegistrations = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        extraRegistrations?.Invoke(services);
        services.AddAiAnalysisModule(configuration);

        return services.BuildServiceProvider();
    }
}
