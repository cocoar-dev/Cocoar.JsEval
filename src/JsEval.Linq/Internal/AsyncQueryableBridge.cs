using System.Collections.Concurrent;
using System.Reflection;

namespace Cocoar.JsEval.Linq.Internal;

/// <summary>
/// Executes a terminal LINQ operation asynchronously when the provider offers
/// one, and falls back to the synchronous operation when it does not.
///
/// There is no async terminal on <see cref="IQueryable{T}"/> in the BCL. Every
/// provider ships its own extension methods instead, and they have converged on
/// one signature — <c>XxxAsync&lt;TSource&gt;(IQueryable&lt;TSource&gt;,
/// CancellationToken)</c> — in <c>Marten.QueryableExtensions</c>,
/// <c>Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions</c> and
/// others alike. Rather than depend on any of them, the method is located at
/// runtime in the assembly the queryable itself comes from.
///
/// Providers with no async terminal (in-memory <c>IQueryable</c>, LINQ2DB) keep
/// working through the synchronous path. Marten 9 is the case that needs this:
/// it refuses synchronous execution outright.
/// </summary>
internal static class AsyncQueryableBridge
{
    private static readonly ConcurrentDictionary<(Assembly Assembly, string Name), MethodInfo?> Methods = new();

    public static Task<TResult> InvokeAsync<TSource, TResult>(
        IQueryable<TSource> source,
        string asyncMethodName,
        Func<IQueryable<TSource>, TResult> synchronousFallback,
        CancellationToken cancellationToken = default)
    {
        var method = Find<TSource, TResult>(source, asyncMethodName);
        if (method is null)
            return Task.FromResult(synchronousFallback(source));

        return (Task<TResult>)method.Invoke(null, [source, cancellationToken])!;
    }

    /// <summary>
    /// Looks for the provider's own <c>XxxAsync</c> extension method. Both the
    /// queryable's assembly and its provider's are searched, because the two are
    /// not always the same one.
    ///
    /// The closed method's return type has to be exactly <c>Task&lt;TResult&gt;</c>.
    /// Providers do not always agree on the shape — Marten's <c>ToListAsync</c>
    /// returns <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c> where EF Core's returns
    /// <c>Task&lt;List&lt;T&gt;&gt;</c> — and a mismatch has to degrade to the
    /// synchronous fallback rather than blow up on an invalid cast at run time.
    /// </summary>
    private static MethodInfo? Find<TSource, TResult>(IQueryable<TSource> source, string name)
    {
        var candidates = source.Provider.GetType().Assembly == source.GetType().Assembly
            ? [source.GetType().Assembly]
            : new[] { source.GetType().Assembly, source.Provider.GetType().Assembly };

        foreach (var assembly in candidates)
        {
            var definition = Methods.GetOrAdd((assembly, name), static key => Search(key.Assembly, key.Name));
            if (definition is null) continue;

            var closed = definition.MakeGenericMethod(typeof(TSource));
            if (closed.ReturnType == typeof(Task<TResult>)) return closed;
        }

        return null;
    }

    private static MethodInfo? Search(Assembly assembly, string name)
    {
        IEnumerable<Type> types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException or ReflectionTypeLoadException)
        {
            // A provider we cannot inspect is treated as one without an async
            // terminal; the synchronous fallback still applies.
            return null;
        }

        foreach (var type in types)
        {
            if (!type.IsAbstract || !type.IsSealed) continue;   // static class

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, name, StringComparison.Ordinal)) continue;
                if (!method.IsGenericMethodDefinition) continue;
                if (method.GetGenericArguments().Length != 1) continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 2) continue;
                if (parameters[1].ParameterType != typeof(CancellationToken)) continue;

                var first = parameters[0].ParameterType;
                if (!first.IsGenericType || first.GetGenericTypeDefinition() != typeof(IQueryable<>)) continue;

                return method;
            }
        }

        return null;
    }
}
