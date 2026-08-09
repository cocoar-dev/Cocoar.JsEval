using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Cocoar.JsEval.Engine;

/// <summary>
/// Declares which CLR members a script may reach. Everything not declared here
/// does not exist as far as the script is concerned, so the reachable surface
/// is what this list says rather than the transitive closure of whatever object
/// the host happened to pass in.
///
/// Members are named through expressions rather than strings so a rename is a
/// compile error instead of a silently narrower sandbox.
/// </summary>
public sealed class JsInteropAllowlist
{
    internal HashSet<MemberInfo> Members { get; } = [];
    internal HashSet<Type> Types { get; } = [];

    /// <summary>Allows one property or field: <c>a.Member((Customer c) =&gt; c.Name)</c>.</summary>
    public JsInteropAllowlist Member<T, TValue>(Expression<Func<T, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Members.Add(ExtractMember(selector.Body));
        return this;
    }

    /// <summary>
    /// Allows one method: <c>a.Method((Customer c) =&gt; c.Greet(default!))</c>.
    /// Argument values are never used — only the signature is read — so any
    /// placeholder works.
    /// </summary>
    public JsInteropAllowlist Method<T>(Expression<Action<T>> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        Members.Add(ExtractMember(call.Body));
        return this;
    }

    /// <inheritdoc cref="Method{T}(Expression{Action{T}})"/>
    public JsInteropAllowlist Method<T, TResult>(Expression<Func<T, TResult>> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        Members.Add(ExtractMember(call.Body));
        return this;
    }

    /// <summary>
    /// Allows every member of a type. Intended for value-like framework types a
    /// rule works with directly — <c>DateTime</c>, <c>TimeSpan</c>, <c>Guid</c> —
    /// where listing each member would be noise. Do not use it on the host's own
    /// service or entity types; that is the transitive reach this list exists to
    /// prevent.
    /// </summary>
    public JsInteropAllowlist Type<T>() => Type(typeof(T));

    /// <inheritdoc cref="Type{T}()"/>
    public JsInteropAllowlist Type(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Types.Add(type);
        return this;
    }

    private static MemberInfo ExtractMember(Expression body)
    {
        // Value types are boxed by the compiler when the selector returns object.
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } convert)
            body = convert.Operand;

        return body switch
        {
            MemberExpression m => m.Member,
            MethodCallExpression c => c.Method,
            _ => throw new ArgumentException(
                $"Expected a property, field or method access, got {body.NodeType}.", nameof(body))
        };
    }
}
