using System;
using Jint;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// Registers the <see cref="CsDateTime"/> class as a JS global so scripts can
/// use its static factories (<c>CsDateTime.Now</c>, <c>CsDateTime.Parse('…')</c>,
/// <c>CsDateTime.From(2024, 6, 15)</c>) directly.
/// </summary>
public static class CsDateTimeGlobals
{
    /// <summary>
    /// Exposes the <see cref="CsDateTime"/> type reference on the engine so JS
    /// can call static members (<c>CsDateTime.Now</c>) and the constructor
    /// (<c>new CsDateTime(…)</c>).
    /// </summary>
    public static void Register(Jint.Engine engine)
    {
        engine.SetValue("CsDateTime", typeof(CsDateTime));
    }
}
