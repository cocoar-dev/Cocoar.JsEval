using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cocoar.JsEval.TsDefinition;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the TsDefinitionService for generating .d.ts IntelliSense definitions.
    /// Requires IJsModuleRegistry to be registered (via AddJsEval).
    /// </summary>
    public static IServiceCollection AddTsDefinition(this IServiceCollection services)
    {
        services.TryAddSingleton<TsDefinitionService>();
        return services;
    }
}
