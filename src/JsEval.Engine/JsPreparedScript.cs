using Acornima.Ast;
using Jint;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// A pre-parsed JavaScript script that can be executed multiple times
/// without re-parsing. Use <see cref="JsEngine.Prepare"/> to create.
/// </summary>
public sealed class JsPreparedScript
{
    internal Prepared<Script> Prepared { get; }

    internal JsPreparedScript(Prepared<Script> prepared) => Prepared = prepared;
}
