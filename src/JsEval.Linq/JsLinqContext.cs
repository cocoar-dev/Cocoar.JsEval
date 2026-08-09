namespace Cocoar.JsEval.Linq;

/// <summary>
/// Ambient per-call context used by <see cref="JsLinqExtensions"/> to reach
/// the currently executing Jint engine for closure resolution. Host sets this
/// via <see cref="Scope"/> before calling into JS and disposes it after.
///
/// Opening a scope is a capability grant, not just plumbing — see the remarks
/// on <see cref="Scope(Jint.Engine, TranslationOptions)"/>. With no scope
/// open, closure resolution is off and a translated rule can reach nothing
/// beyond the entity it queries.
/// </summary>
public static class JsLinqContext
{
    private static readonly AsyncLocal<State?> _state = new();

    /// <summary>
    /// The Jint engine of the current <see cref="Scope"/> (if any) — used by the
    /// built-in extension methods and available to custom wrappers that call
    /// <see cref="JsExpressionTranslator.Translate{T, TResult}(Jint.Native.JsValue, Jint.Engine?)"/>
    /// from inside a JS-invoked method.
    /// </summary>
    public static Jint.Engine? CurrentEngine => _state.Value?.Engine;

    /// <summary>
    /// The translation options of the current <see cref="Scope"/> (if any).
    /// </summary>
    public static TranslationOptions? CurrentOptions => _state.Value?.Options;

    /// <summary>
    /// Enter a scope for a given engine. Disposing the returned handle restores
    /// the previous context. Safe for nesting.
    /// </summary>
    /// <remarks>
    /// The scoped engine decides what a translated rule can reach. Closure
    /// resolution looks up every free identifier in it, and members are then
    /// resolved on the result by reflection — any public property, and any
    /// public method with arguments the rule chooses. The rule is never
    /// executed as JavaScript, but the expression tree it produces is evaluated
    /// later by the LINQ provider, and a call on a captured host object runs at
    /// that point. Scoping an engine that carries modules or
    /// <c>SetValue</c>-registered services makes them callable from every rule
    /// translated inside the scope.
    ///
    /// That is the intended convenience for rules the application itself
    /// authors. For rules written by tenants or end users, either open no scope
    /// — a free identifier then fails translation with <c>Unresolved
    /// identifier</c> instead of resolving — or scope a bare engine with
    /// nothing registered on it. Note also that every public property of the
    /// queried entity is reachable, so project to a DTO carrying only what
    /// rules are meant to see.
    /// </remarks>
    public static IDisposable Scope(Jint.Engine engine, TranslationOptions? options = null)
    {
        var previous = _state.Value;
        _state.Value = new State { Engine = engine, Options = options };
        return new Restore(previous);
    }

    /// <summary>Convenience overload that takes a <see cref="Engine.JsEngine"/> directly.</summary>
    public static IDisposable Scope(Engine.JsEngine engine, TranslationOptions? options = null)
        => Scope(engine.UnderlyingEngine, options);

    private sealed class State
    {
        public Jint.Engine? Engine;
        public TranslationOptions? Options;
    }

    private sealed class Restore : IDisposable
    {
        private readonly State? _previous;
        public Restore(State? previous) => _previous = previous;
        public void Dispose() => _state.Value = _previous;
    }
}
