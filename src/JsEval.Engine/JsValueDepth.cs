using System;
using System.Collections.Generic;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Descriptors;
// Aliased as JintFunction, not JsFunction: Cocoar.JsEval.JsFunction is a
// different type, and naming the alias after it made `is JsFunction` bind to
// that one instead — a check that can never match, which the compiler reported
// as CS0184 and which silently stopped functions from being skipped below.
using JintFunction = Jint.Native.Function.Function;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// Measures how deeply a JavaScript value nests, without recursing itself.
///
/// Jint's JSON serializer recurses once per level, so a value a script nested a
/// few thousand deep exhausts the .NET stack and takes the process down with an
/// uncatchable <c>StackOverflowException</c>. Callers check the shape here
/// first and report a normal error instead.
/// </summary>
internal static class JsValueDepth
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="root"/> nests deeper than
    /// <paramref name="maxDepth"/>.
    ///
    /// The frame stack doubles as the current path, so the deepest chain is
    /// measured exactly and a back-reference is recognised as a cycle rather
    /// than mistaken for unbounded nesting. Functions are skipped because
    /// <c>JSON.stringify</c> omits them, which also keeps the walk out of the
    /// prototype/constructor cycle every function object carries. Accessor
    /// properties are treated as leaves; the serializer invokes them later
    /// under the engine's execution constraints.
    /// </summary>
    public static bool Exceeds(JsValue root, int maxDepth)
    {
        if (root is not ObjectInstance rootObject || root is JintFunction)
            return false;

        var frames = new Stack<Frame>();
        if (!TryDescend(frames, rootObject, maxDepth))
            return true;

        while (frames.Count > 0)
        {
            var frame = frames.Peek();
            if (!frame.Properties.MoveNext())
            {
                frames.Pop();
                continue;
            }

            var descriptor = frame.Properties.Current.Value;
            if (descriptor.Get is not null || descriptor.Set is not null)
                continue;

            if (descriptor.Value is not ObjectInstance child || descriptor.Value is JintFunction)
                continue;

            if (IsOnCurrentPath(frames, child))
                continue; // a cycle — left to the serializer, which names it precisely

            if (!TryDescend(frames, child, maxDepth))
                return true;
        }

        return false;
    }

    private static bool TryDescend(Stack<Frame> frames, ObjectInstance node, int maxDepth)
    {
        if (frames.Count + 1 > maxDepth)
            return false;

        frames.Push(new Frame(node, node.GetOwnProperties().GetEnumerator()));
        return true;
    }

    private static bool IsOnCurrentPath(Stack<Frame> frames, ObjectInstance candidate)
    {
        foreach (var frame in frames)
        {
            if (ReferenceEquals(frame.Node, candidate))
                return true;
        }

        return false;
    }

    private readonly record struct Frame(
        ObjectInstance Node,
        IEnumerator<KeyValuePair<JsValue, PropertyDescriptor>> Properties);
}
