using System;
using System.Threading.Tasks;
using Cocoar.JsEval;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// Consumer-facing interface for the JavaScript engine.
/// Enables testability and mocking of the engine in consumer code.
///
/// Two execution modes are available:
///
/// <b>ExecuteAsync</b> — Standard. Full ES module system with import/export, async/await, and registered modules.
/// Use this when you don't control the script content or when scripts need modules.
///
/// <b>Evaluate</b> — Lightweight. Plain JavaScript without module system.
/// Use this only when you know the scripts don't need import/export or modules.
/// Supports pre-parsed scripts for maximum throughput.
/// </summary>
public interface IJsEngine : IScriptEngine, IDisposable, IAsyncDisposable
{
    JsEngineOptions Options { get; }

    // --- ExecuteAsync: Standard execution with full module system ---

    /// <summary>
    /// Executes a script as an ES module with full module system support.
    /// Supports import/export, registered modules, async/await, and all JS features.
    /// This is the standard execution method — use this when you don't know what the script contains.
    /// </summary>
    Task ExecuteAsync(string script);

    // --- Evaluate: Lightweight execution without module system ---

    /// <summary>
    /// Evaluates a plain JavaScript script synchronously. No module system, no import/export.
    /// Variables set via <see cref="SetValue"/> are available as globals.
    /// Use this when you know the script doesn't need modules or async.
    /// For repeated execution, use <see cref="Prepare"/> + <see cref="Evaluate(JsPreparedScript)"/>.
    /// </summary>
    void Evaluate(string script);

    /// <summary>
    /// Evaluates a pre-parsed script synchronously. Avoids re-parsing on every call.
    /// Fastest execution path. Use <see cref="Prepare"/> to create the prepared script.
    /// </summary>
    void Evaluate(JsPreparedScript prepared);

    /// <summary>
    /// Evaluates a plain JavaScript script asynchronously. Supports top-level await but no module system.
    /// Use this when the script may contain await expressions but doesn't need import/export.
    /// </summary>
    Task EvaluateAsync(string script);

    /// <summary>
    /// Pre-parses a JavaScript script for repeated execution via <see cref="Evaluate(JsPreparedScript)"/>.
    /// The returned object is thread-safe and can be cached and shared across engine instances.
    /// </summary>
    static JsPreparedScript Prepare(string script) => JsEngine.Prepare(script);

    // --- Value access ---

    void SetValue(string name, object value);
    T? GetValue<T>(string name);
    string GetValueAsJson(string name);
    object InvokeFunction(string name, params object[] args);
    JsFunction? GetFunction(string name);
    void Stop();
}
