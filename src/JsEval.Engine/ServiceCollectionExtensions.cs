using System;
using Cocoar.JsEval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Cocoar.JsEval.Engine;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the JsEval engine and module system with default options.
    /// </summary>
    public static IServiceCollection AddJsEval(this IServiceCollection services)
    {
        return AddJsEval(services, _ => { });
    }

    /// <summary>
    /// Registers the JsEval engine and module system with a fluent builder.
    /// <code>
    /// services.AddJsEval(js => js
    ///     .EnableFetch()
    ///     .AddModule&lt;HttpModule&gt;()
    ///     .AddModule&lt;CommonModule&gt;());
    /// </code>
    /// </summary>
    public static IServiceCollection AddJsEval(this IServiceCollection services, Action<JsEvalBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new JsEvalBuilder();
        configure(builder);

        services.TryAddSingleton(builder.Options);
        services.TryAddSingleton<IJsModuleRegistry>(builder.ModuleRegistry);
        services.AddTransient<JsEngine>(sp => new JsEngine(
            sp,
            sp.GetRequiredService<IJsModuleRegistry>(),
            sp.GetRequiredService<JsEngineOptions>(),
            sp.GetService<ILogger<JsEngine>>()));

        return services;
    }
}
