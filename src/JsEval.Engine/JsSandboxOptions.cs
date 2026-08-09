using System;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// Resource limits for <see cref="JsSandbox"/>. These limits are defense in
/// depth for in-process execution; the sandbox deliberately exposes no switch
/// that enables CLR interop or host capabilities.
/// </summary>
public sealed class JsSandboxOptions
{
    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan RegexTimeout { get; init; } = TimeSpan.FromMilliseconds(50);
    public int MaxStatements { get; init; } = 100_000;
    public long MemoryLimitBytes { get; init; } = 16 * 1024 * 1024;
    public int MaxRecursionDepth { get; init; } = 64;
    public int MaxExecutionStackCount { get; init; } = 256;
    public uint MaxArraySize { get; init; } = 10_000;
    public int MaxScriptBytes { get; init; } = 64 * 1024;
    public int MaxInputBytes { get; init; } = 256 * 1024;
    public int MaxOutputBytes { get; init; } = 256 * 1024;

    /// <summary>
    /// Maximum nesting depth of a value crossing the boundary in either
    /// direction. This is a safety limit, not a preference: JSON serialization
    /// recurses once per level, so an unbounded structure exhausts the .NET
    /// stack and takes down the process with an uncatchable
    /// <c>StackOverflowException</c>. The default matches Jint's own
    /// <c>Json.MaxParseDepth</c> and <c>System.Text.Json</c>'s read depth, so
    /// all three agree on what is representable.
    /// </summary>
    public int MaxDepth { get; init; } = 64;
}
