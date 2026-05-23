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

        // IJsModuleBuilder owns the IServiceProvider-based module activation —
        // keeping it off JsEngine's ctor means downstream codegen (Wolverine,
        // AOT analyzers) sees only typed dependencies on the engine itself.
        // Registered type-based (not via lambda factory) so Wolverine 6's
        // strict ServiceLocationPolicy.NotAllowed can walk the ctor statically
        // — opaque-factory closures fail that check regardless of how clean
        // the ctor signature is.
        services.TryAddScoped<IJsModuleBuilder, JsModuleBuilder>();

        // Scoped, not Transient: Jint engines are not thread-safe, and multiple
        // services resolving JsEngine in the same request should share one engine
        // so globals set via SetValue are visible across them. Consumers that
        // need an isolated engine can construct one explicitly with `new JsEngine(...)`.
        // Type-based registration — see note on IJsModuleBuilder above. The
        // optional `ILogger<JsEngine>? = null` ctor parameter resolves via
        // ActivatorUtilities' default-value handling when logging is not
        // registered.
        services.AddScoped<JsEngine>();

        return services;
    }
}
