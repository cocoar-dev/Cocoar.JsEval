namespace Cocoar.JsEval.Linq;

/// <summary>
/// Ambient per-call context used by <see cref="JsLinqExtensions"/> to reach
/// the currently executing Jint engine (needed by the translator for closure resolution).
/// Host sets this via <see cref="Scope"/> before calling into JS and disposes it after.
/// </summary>
public static class JsLinqContext
{
    private static readonly AsyncLocal<State> _state = new();

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
