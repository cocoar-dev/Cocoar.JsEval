using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cocoar.JsEval.TypeScript;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the TypeScript transpiler as a singleton service.
    /// Use TsTranspiler.Transpile() to compile TypeScript to JavaScript.
    /// </summary>
    public static IServiceCollection AddTsTranspiler(this IServiceCollection services)
    {
        services.TryAddSingleton<TsTranspiler>();
        return services;
    }
}
