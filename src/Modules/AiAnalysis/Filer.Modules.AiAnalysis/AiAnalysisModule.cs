using Filer.Modules.AiAnalysis.Contracts;
using Filer.Modules.BackgroundJobs.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Filer.Modules.AiAnalysis;

/// <summary>
/// Registration entry point for the AI Analysis module. The host invokes
/// <see cref="AddAiAnalysisModule"/> only; it never reaches into module internals
/// (10-solution-structure.md). The module maps no endpoints: other modules reach it
/// through <see cref="IAIAnalysisProvider"/>, consumed by the background worker
/// (06-ai-analysis-pipeline.md).
/// </summary>
public static class AiAnalysisModule
{
    public static IServiceCollection AddAiAnalysisModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiAnalysisOptions>()
            .Bind(configuration.GetSection(AiAnalysisOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Text-extraction tuning for the job handler (#53). The section is
        // optional — every option has a safe default.
        services.AddOptions<TextExtractionOptions>()
            .Bind(configuration.GetSection(TextExtractionOptions.SectionName))
            .Validate(
                options => options.MaxChars > 0,
                "AiAnalysis:TextExtraction:MaxChars must be positive.")
            .ValidateOnStart();

        // The real analysis job handler (#53): registered before the BackgroundJobs
        // module so its TryAddScoped no-op fallback never wins (Program.cs ordering).
        // Unconditional — it orchestrates whichever IAIAnalysisProvider is selected.
        services.AddScoped<IAnalysisJobHandler, AnalysisJobHandler>();

        // Eager read for provider selection, mirroring AddStorageModule: registration
        // shape is decided while the builder is still composing. The section is
        // optional — every option has a safe default.
        AiAnalysisOptions options = configuration.GetSection(AiAnalysisOptions.SectionName).Get<AiAnalysisOptions>()
            ?? new AiAnalysisOptions();

        // Provider selected by configuration (06): the host decides which adapter
        // runs; everything else sees only IAIAnalysisProvider.
        switch (options.Provider)
        {
            case AiAnalysisOptions.FakeProviderName:
                services.AddSingleton<IAIAnalysisProvider, FakeAnalysisProvider>();
                break;
            case AiAnalysisOptions.OllamaProviderName:
                AddOllamaProvider(services);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown AI analysis provider '{options.Provider}'. Supported providers: " +
                    $"'{AiAnalysisOptions.FakeProviderName}', '{AiAnalysisOptions.OllamaProviderName}'.");
        }

        return services;
    }

    /// <summary>
    /// Wires the no-egress Ollama adapter as a typed HttpClient (06): base address and
    /// timeout come from <see cref="OllamaOptions"/>, never literals (13, Options
    /// pattern). The options are validated here — only on the Ollama path — so a
    /// misconfigured local provider fails fast rather than at first inference. The
    /// typed-client factory reads validated options from DI so validation runs before
    /// the base address is constructed.
    /// </summary>
    private static void AddOllamaProvider(IServiceCollection services)
    {
        services.AddOptions<AiAnalysisOptions>()
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Ollama.BaseUrl)
                           && Uri.TryCreate(options.Ollama.BaseUrl, UriKind.Absolute, out _),
                "AiAnalysis:Ollama:BaseUrl must be a non-empty absolute URL.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Ollama.Model),
                "AiAnalysis:Ollama:Model must not be empty.")
            .Validate(
                options => options.Ollama.TimeoutSeconds > 0,
                "AiAnalysis:Ollama:TimeoutSeconds must be positive.")
            .Validate(
                options => options.Ollama.MaxPromptChars > 0,
                "AiAnalysis:Ollama:MaxPromptChars must be positive.")
            .ValidateOnStart();

        services.AddHttpClient<IAIAnalysisProvider, OllamaAnalysisProvider>((serviceProvider, client) =>
        {
            // Resolving the options runs the Validate rules above, so an invalid
            // BaseUrl/Model/timeout fails as a validation error before the URI is built.
            OllamaOptions current = serviceProvider.GetRequiredService<IOptions<AiAnalysisOptions>>().Value.Ollama;
            client.BaseAddress = new Uri(current.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(current.TimeoutSeconds);
        });
    }
}
