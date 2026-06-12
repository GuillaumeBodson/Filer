using Filer.Modules.AiAnalysis.Contracts;
using Filer.Modules.BackgroundJobs.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
            default:
                throw new InvalidOperationException(
                    $"Unknown AI analysis provider '{options.Provider}'. Supported providers: '{AiAnalysisOptions.FakeProviderName}'.");
        }

        return services;
    }
}
