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

        // Apply any deferred DI registrations (e.g. IJsTsDefinitionContributor
        // singletons queued by AddLinq/AddTsDefinitionContributor on the builder).
        foreach (var register in builder.DeferredRegistrations)
            register(services);

        // Scoped, not Transient: Jint engines are not thread-safe, and multiple
        // services resolving JsEngine in the same request should share one engine
        // so globals set via SetValue are visible across them. Consumers that
        // need an isolated engine can construct one explicitly with `new JsEngine(...)`.
        services.AddScoped<JsEngine>(sp => new JsEngine(
            sp,
            sp.GetRequiredService<IJsModuleRegistry>(),
            sp.GetRequiredService<JsEngineOptions>(),
            sp.GetService<ILogger<JsEngine>>()));

        return services;
    }
}
