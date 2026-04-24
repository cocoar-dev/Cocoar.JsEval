using Acornima.Ast;
using Jint;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// A pre-parsed ES module script that can be executed multiple times
/// without re-parsing. Supports <c>import</c> / <c>export</c>. Each execution
/// runs top-level statements fresh (unlike a cached module import).
/// Use <see cref="JsEngine.PrepareModule"/> to create.
/// </summary>
public sealed class JsPreparedModule
{
    internal Prepared<Module> Prepared { get; }

    internal JsPreparedModule(Prepared<Module> prepared) => Prepared = prepared;
}
