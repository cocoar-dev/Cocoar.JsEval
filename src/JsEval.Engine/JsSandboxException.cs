using System;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// Thrown when a sandboxed script fails, or when its result cannot be handed
/// back to the host. Every failure originating inside <see cref="JsSandbox"/>
/// execution surfaces as this type — script errors, timeouts, statement,
/// memory, recursion and regex limits, nesting-depth and output-size
/// violations, and values the script left in a non-serializable state. The
/// originating exception is kept as <see cref="Exception.InnerException"/>.
///
/// Misuse of the API by the host is deliberately not wrapped: an invalid value
/// name or oversized input still throws <see cref="ArgumentException"/>, since
/// that is a bug in the calling code rather than a script failure.
/// </summary>
public sealed class JsSandboxException : Exception
{
    public JsSandboxException(string message)
        : base(message)
    {
    }

    public JsSandboxException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
