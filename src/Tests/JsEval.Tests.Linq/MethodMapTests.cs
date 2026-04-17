using System.Linq.Expressions;
using Cocoar.JsEval.Linq;
using Cocoar.JsEval.Linq.MethodMapping;
using Jint;
using Xunit;

namespace Cocoar.JsEval.Tests.Linq;

public class MethodMapTests
{
    [Fact]
    public void DefaultMap_MapsStartsWith()
    {
        var map = new DefaultJsMethodMap();
        var target = Expression.Parameter(typeof(string), "s");
        var req = new MethodResolveRequest
        {
            Target = target,
            JsMethodName = "startsWith",
            Arguments = new[] { (Expression)Expression.Constant("A") },
            TranslateSubExpression = e => e,
        };
        Assert.True(map.TryResolve(req, out var result));
        var call = Assert.IsAssignableFrom<MethodCallExpression>(result);
        Assert.Equal(nameof(string.StartsWith), call.Method.Name);
    }

    [Fact]
    public void DefaultMap_UnknownJsMethod_ReturnsFalse()
    {
        var map = new DefaultJsMethodMap();
        var req = new MethodResolveRequest
        {
            Target = Expression.Parameter(typeof(string), "s"),
            JsMethodName = "someNonExistentMethod",
            Arguments = Array.Empty<Expression>(),
            TranslateSubExpression = e => e,
        };
        Assert.False(map.TryResolve(req, out var _));
    }

    [Fact]
    public void CompositeMap_FirstMatch_Wins()
    {
        var custom = new FakeMap("startsWith", () => Expression.Constant(true));
        var composite = new CompositeJsMethodMap(custom, new DefaultJsMethodMap());

        var req = new MethodResolveRequest
        {
            Target = Expression.Parameter(typeof(string), "s"),
            JsMethodName = "startsWith",
            Arguments = new[] { (Expression)Expression.Constant("A") },
            TranslateSubExpression = e => e,
        };
        Assert.True(composite.TryResolve(req, out var result));
        Assert.True(result is ConstantExpression c && (bool)c.Value!);
    }

    [Fact]
    public void CompositeMap_FallsThroughToDefault_WhenCustomDoesNotMatch()
    {
        var custom = new FakeMap("onlyThisName", () => Expression.Constant(true));
        var composite = new CompositeJsMethodMap(custom, new DefaultJsMethodMap());

        var req = new MethodResolveRequest
        {
            Target = Expression.Parameter(typeof(string), "s"),
            JsMethodName = "startsWith",
            Arguments = new[] { (Expression)Expression.Constant("A") },
            TranslateSubExpression = e => e,
        };
        Assert.True(composite.TryResolve(req, out var result));
        var call = Assert.IsAssignableFrom<MethodCallExpression>(result);
        Assert.Equal(nameof(string.StartsWith), call.Method.Name);
    }

    [Fact]
    public void CustomMethodMap_TranslatorUsesItEndToEnd()
    {
        // Custom: "shout" on string -> ToUpper().
        var options = new TranslationOptions
        {
            MethodMap = new CompositeJsMethodMap(
                new FakeMap("shout", target: typeof(string),
                    make: t => Expression.Call(
                        t,
                        typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!)),
                new DefaultJsMethodMap())
        };
        var engine = new Jint.Engine();
        var jsFn = engine.Evaluate("(u) => u.Name.shout() === 'ALICE'");
        var expr = JsExpressionTranslator.Translate<TestUser, bool>(jsFn, engine, options);
        Assert.Contains("ToUpper", expr.ToString(), StringComparison.Ordinal);
    }

    // --- helper ---

    private sealed class FakeMap : IJsMethodMap
    {
        private readonly string _name;
        private readonly Func<Expression>? _result;
        private readonly Type? _target;
        private readonly Func<Expression, Expression>? _make;

        public FakeMap(string name, Func<Expression> result) { _name = name; _result = result; }

        public FakeMap(string name, Type target, Func<Expression, Expression> make)
        { _name = name; _target = target; _make = make; }

        public bool TryResolve(MethodResolveRequest request, out Expression? result)
        {
            if (request.JsMethodName != _name) { result = null; return false; }
            if (_target != null && request.Target.Type != _target) { result = null; return false; }
            result = _make != null ? _make(request.Target) : _result!.Invoke();
            return true;
        }
    }
}
