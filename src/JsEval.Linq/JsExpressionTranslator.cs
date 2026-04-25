using System.Linq.Expressions;
using System.Reflection;
using Acornima;
using Acornima.Ast;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Linq.Internal;
using Cocoar.JsEval.Linq.MethodMapping;
using Jint;
using Jint.Native;
using Jint.Native.Function;

using AstExpr = Acornima.Ast.Expression;
using AstMember = Acornima.Ast.MemberExpression;
using AstUnary = Acornima.Ast.UnaryExpression;
using AstConditional = Acornima.Ast.ConditionalExpression;
using LinqExpr = System.Linq.Expressions.Expression;

namespace Cocoar.JsEval.Linq;

/// <summary>
/// Translates a JavaScript arrow function (Jint/Acornima AST) into a .NET
/// <see cref="Expression{TDelegate}"/> tree that any LINQ provider can consume.
/// </summary>
public static class JsExpressionTranslator
{
    /// <summary>Convenience overload with defaults.</summary>
    public static Expression<Func<T, TResult>> Translate<T, TResult>(JsValue function, Jint.Engine? engine = null)
        => Translate<T, TResult>(function, engine, options: null);

    /// <summary>Convenience overload that takes a <see cref="JsEngine"/> directly.</summary>
    public static Expression<Func<T, TResult>> Translate<T, TResult>(JsValue function, JsEngine engine, TranslationOptions? options = null)
        => Translate<T, TResult>(function, engine?.UnderlyingEngine, options);

    /// <summary>Full translate overload with pluggable options.</summary>
    public static Expression<Func<T, TResult>> Translate<T, TResult>(JsValue function, Jint.Engine? engine, TranslationOptions? options)
    {
        var (parameter, body) = TranslateCore(function, typeof(T), engine, options);
        if (body.Type != typeof(TResult))
        {
            // `bool?` → `bool` in a boolean-returning lambda: treat null as false
            // (JS-truthy semantics). Mirrors writing `== true` explicitly in C# —
            // the form you'd be forced into anyway to use `?.` inside Where(…).
            if (typeof(TResult) == typeof(bool) && body.Type == typeof(bool?))
                body = NormalizeToBool(body);
            else
                body = LinqExpr.Convert(body, typeof(TResult));
        }
        return LinqExpr.Lambda<Func<T, TResult>>(body, parameter);
    }

    /// <summary>
    /// Translates a JS arrow function without requiring the caller to specify <c>TResult</c>.
    /// Returns a non-generic <see cref="LambdaExpression"/> whose <see cref="LambdaExpression.ReturnType"/>
    /// is inferred from the translated body. Useful for key selectors in <c>OrderBy</c>/<c>Select</c>
    /// where the return type is whatever the JS body happens to produce.
    /// </summary>
    public static LambdaExpression TranslateLambda<T>(JsValue function, Jint.Engine? engine = null, TranslationOptions? options = null)
    {
        var (parameter, body) = TranslateCore(function, typeof(T), engine, options);
        var delegateType = typeof(Func<,>).MakeGenericType(typeof(T), body.Type);
        return LinqExpr.Lambda(delegateType, body, parameter);
    }

    /// <summary>Convenience overload that takes a <see cref="JsEngine"/> directly.</summary>
    public static LambdaExpression TranslateLambda<T>(JsValue function, JsEngine engine, TranslationOptions? options = null)
        => TranslateLambda<T>(function, engine?.UnderlyingEngine, options);

    private static (ParameterExpression parameter, LinqExpr body) TranslateCore(
        JsValue function, Type parameterType, Jint.Engine? engine, TranslationOptions? options)
    {
        if (function is not ScriptFunction sf)
            throw new ArgumentException($"Expected a JS function, got {function.GetType().Name}", nameof(function));

        options ??= new TranslationOptions();
        var decl = sf.FunctionDeclaration;
        var parameter = BuildParameter(decl, parameterType);
        var ctx = new Context(engine, options).Push(parameter);
        var body = Visit(GetBodyExpression(decl), ctx);
        return (parameter, body);
    }

    // -------------------- internals --------------------

    internal sealed class Context
    {
        public readonly Jint.Engine? Engine;
        public readonly TranslationOptions Options;
        public readonly List<ParameterExpression> ParamStack = new();

        // Active narrowing frames per parameter, pushed/popped around AND right-hand sides.
        // Each frame is the set of concrete types the parameter may be (from TypeIs nodes
        // collected from the AND's left operand, across both AND and OR sub-trees).
        private readonly Dictionary<ParameterExpression, Stack<List<Type>>> _narrowings = new();

        private IReadOnlyDictionary<string, Type>? _namespaceMapped;

        public Context(Jint.Engine? engine, TranslationOptions options)
        { Engine = engine; Options = options; }

        public Context Push(ParameterExpression p) { ParamStack.Add(p); return this; }

        public IReadOnlyDictionary<string, Type> GetNamespaceMapped()
        {
            if (_namespaceMapped != null) return _namespaceMapped;
            if (Options.NamespaceMappings.Count == 0 || Options.DiscriminatorMappings.Count == 0)
                return _namespaceMapped = new Dictionary<string, Type>();

            var types = Options.DiscriminatorMappings.Select(m => m.ConcreteType).Where(t => t != null).Select(t => t!)
                .Concat(Options.DiscriminatorMappings.Select(m => m.BaseType))
                .Distinct();
            var resolutions = TypeAliasResolver.Resolve(types, Options.TypeAliases, Options.NamespaceMappings);
            var dict = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var r in resolutions)
            {
                if (r.Source != TypeAliasResolutionSource.NamespaceMapping) continue;
                var key = string.IsNullOrEmpty(r.MappedNamespace)
                    ? r.ShortName
                    : r.MappedNamespace + "." + r.ShortName;
                dict.TryAdd(key, r.Type);
            }
            return _namespaceMapped = dict;
        }

        public ParameterExpression? Find(string name) =>
            ParamStack.LastOrDefault(p => p.Name == name);

        public void PushNarrowing(ParameterExpression param, List<Type> types)
        {
            if (!_narrowings.TryGetValue(param, out var stack))
                _narrowings[param] = stack = new();
            stack.Push(types);
        }

        public void PopNarrowing(ParameterExpression param)
        {
            if (_narrowings.TryGetValue(param, out var stack) && stack.Count > 0)
                stack.Pop();
        }

        public List<Type>? GetNarrowings(ParameterExpression param) =>
            _narrowings.TryGetValue(param, out var stack) && stack.Count > 0 ? stack.Peek() : null;
    }

    private static ParameterExpression BuildParameter(IFunction decl, Type t)
    {
        if (decl.Params.Count != 1)
            throw new NotSupportedException(
                $"Only single-parameter lambdas are supported (got {decl.Params.Count} parameters). " +
                "LINQ providers (Marten/EF/LINQ2DB/...) accept only unary predicates on IQueryable<T>; " +
                "multi-argument lambdas like (item, index) => ... exist only on IEnumerable<T>. " +
                "Capture additional values through closures instead.");

        if (decl.Params[0] is not Identifier paramId)
        {
            var kind = decl.Params[0].GetType().Name;
            throw new NotSupportedException(
                $"Parameter destructuring is not supported (got {kind}). " +
                "Use direct property access on the parameter instead — " +
                "e.g. write  u => u.Name === 'A'  rather than  ({ Name }) => Name === 'A'.");
        }

        return LinqExpr.Parameter(t, paramId.Name);
    }

    private static AstExpr GetBodyExpression(IFunction decl) => decl.Body switch
    {
        AstExpr e => e,
        BlockStatement { Body.Count: 1 } b when b.Body[0] is ReturnStatement { Argument: not null } rs => rs.Argument,
        _ => throw new NotSupportedException($"Body shape {decl.Body.GetType().Name} not supported")
    };

    private static LinqExpr Visit(AstExpr node, Context ctx) => node switch
    {
        Identifier id                  => VisitIdentifier(id, ctx),
        AstMember me                   => VisitMember(me, ctx),
        CallExpression ce              => VisitCall(ce, ctx),
        ChainExpression ch             => VisitChain(ch, ctx),
        StringLiteral sl               => LinqExpr.Constant(sl.Value),
        NumericLiteral nl              => LinqExpr.Constant(nl.Value),
        BooleanLiteral bl              => LinqExpr.Constant(bl.Value),
        NullLiteral                    => LinqExpr.Constant(null),
        NonLogicalBinaryExpression nbe => VisitBinary(nbe, ctx),
        LogicalExpression le           => VisitLogical(le, ctx),
        AstUnary ue             => VisitUnary(ue, ctx),
        AstConditional cond     => VisitConditional(cond, ctx),
        ArrowFunctionExpression        => throw new InvalidOperationException(
            "ArrowFunctionExpression must be visited inside a method call"),
        _ => throw new NotSupportedException($"AST node {node.GetType().Name} not supported")
    };

    private static LinqExpr VisitIdentifier(Identifier id, Context ctx)
    {
        var p = ctx.Find(id.Name);
        if (p != null) return p;

        if (ctx.Engine != null)
        {
            var val = ctx.Engine.GetValue(id.Name);
            if (!val.IsUndefined())
            {
                var clr = val.ToObject();
                return LinqExpr.Constant(clr, clr?.GetType() ?? typeof(object));
            }
        }
        throw new InvalidOperationException($"Unresolved identifier '{id.Name}' (pass Engine for closure support)");
    }

    private static LinqExpr VisitMember(AstMember me, Context ctx)
    {
        var target = Visit((AstExpr)me.Object, ctx);
        var propName = ResolveMemberName(me);

        var prop = ReflectionCache.GetProperty(target.Type, propName);
        if (prop != null)
            return LinqExpr.Property(target, prop);

        // Property not on the declared type — try narrowed subtypes (from active
        // discriminator narrowing frames pushed by VisitLogical AND).
        if (target is ParameterExpression param)
        {
            var narrowings = ctx.GetNarrowings(param);
            if (narrowings != null)
            {
                var narrowed = TryResolveViaIntersection(param, propName, narrowings);
                if (narrowed != null) return narrowed;
            }
        }

        throw new InvalidOperationException($"Property '{propName}' not found on {target.Type.Name}");
    }

    /// <summary>
    /// Resolves <paramref name="propName"/> across all types in <paramref name="narrowings"/>.
    /// Returns <c>null</c> if any type lacks the property; throws if the property exists on
    /// all types but with conflicting CLR types. On success emits
    /// <c>Property(Convert(param, narrowings[0]), prop)</c>.
    /// </summary>
    private static LinqExpr? TryResolveViaIntersection(
        ParameterExpression param, string propName, List<Type> narrowings)
    {
        PropertyInfo? resolved = null;
        foreach (var nt in narrowings)
        {
            var p = ReflectionCache.GetProperty(nt, propName);
            if (p == null) return null;
            if (resolved == null)
                resolved = p;
            else if (resolved.PropertyType != p.PropertyType)
                throw new InvalidOperationException(
                    $"Ambiguous property '{propName}': CLR type differs across narrowed subtypes " +
                    $"({resolved.DeclaringType!.Name}: {resolved.PropertyType.Name} vs " +
                    $"{p.DeclaringType!.Name}: {p.PropertyType.Name})");
        }
        return resolved == null ? null
            : LinqExpr.Property(LinqExpr.Convert(param, narrowings[0]), resolved);
    }

    /// <summary>
    /// Resolves a member access to a CLR property name.
    /// Supports <c>x.Name</c> (Identifier) and <c>x['Name']</c> (computed string literal),
    /// but rejects truly dynamic <c>x[varName]</c> — that cannot be translated to an Expression tree.
    /// </summary>
    private static string ResolveMemberName(AstMember me) => me.Property switch
    {
        Identifier id when !me.Computed            => id.Name,    // x.Name
        StringLiteral sl when me.Computed          => sl.Value,   // x['Name']
        _ => throw new NotSupportedException(
            "Only static member access is supported: `x.Name` or `x['Name']` with a string literal. " +
            "Dynamic access like `x[variable]` cannot be translated to an Expression tree because " +
            "LINQ providers need the property resolved at translation time, not at evaluation time.")
    };

    private static LinqExpr VisitCall(CallExpression ce, Context ctx)
    {
        if (ce.Callee is not AstMember callee)
            throw new NotSupportedException("Only method calls on members supported");

        // Intercept `linq.decimal('...')`, `linq.double('...')`, `linq.int('...')`,
        // `linq.long('...')` at the AST level — producing a precise typed
        // ConstantExpression instead of going through Jint's runtime (which would
        // marshal decimal → JS number and lose precision).
        if (TryInterceptLinqTypedLiteral(ce, out var interception))
            return interception!;

        // Intercept `Type.Is(param, 'value')` and `Type.IsOneOf(param, ['v1','v2'])`.
        if (TryInterceptDiscriminatorIs(ce, callee, ctx, out var typeIs))
            return typeIs!;
        if (TryInterceptDiscriminatorIsOneOf(ce, callee, ctx, out var typeIsOneOf))
            return typeIsOneOf!;

        var target = Visit((AstExpr)callee.Object, ctx);
        return VisitCallOnTarget(target, callee, ce, ctx);
    }

    private static LinqExpr VisitCallOnTarget(LinqExpr target, AstMember callee, CallExpression ce, Context ctx)
    {
        if (callee.Property is not Identifier methodId)
            throw new NotSupportedException("Computed method access not supported");

        // string implements IEnumerable<char>; don't treat it as an array — string methods win.
        if (target.Type != typeof(string)
            && TryGetCollectionElementType(target.Type, out var elementType)
            && IsArrayMethod(methodId.Name))
            return TranslateArrayMethod(target, elementType!, methodId.Name, ce.Arguments, ctx);

        var args = ce.Arguments.Select(a => Visit((AstExpr)a!, ctx)).ToArray();

        var request = new MethodResolveRequest
        {
            Target = target,
            JsMethodName = methodId.Name,
            Arguments = args,
            TranslateSubExpression = e => e
        };
        if (ctx.Options.MethodMap.TryResolve(request, out var mapped) && mapped != null)
            return mapped;

        // Fallback: reflection on the CLR type (IgnoreCase so JS `.addDays(7)`
        // maps to DateTime.AddDays, CsDateTime.AddDays, etc. — Jint's CLR interop
        // is case-insensitive by default and we want LINQ translation to match).
        var argTypes = args.Select(a => a.Type).ToArray();
        var method = ReflectionCache.GetMethod(target.Type, methodId.Name, argTypes)
            ?? throw new InvalidOperationException(
                $"Method '{methodId.Name}' not found on {target.Type.Name} with {args.Length} arg(s)");
        return LinqExpr.Call(target, method, args);
    }

    /// <summary>
    /// Translates a JS optional-chain expression (<c>a?.b.c</c>, <c>a.b?.c.d</c>,
    /// <c>a?.b()</c>). All <c>Optional = true</c> nodes in the chain contribute
    /// null-guards on their immediately-preceding value; if any guard is null the
    /// whole chain short-circuits to <c>null</c>.
    ///
    /// All guards are combined into a **single** positive <see cref="LinqExpr.Condition"/>
    /// (<c>g1 != null &amp;&amp; g2 != null ? body : null</c>) instead of nested
    /// per-guard <c>IIF</c>s. Most LINQ providers (Marten in particular) refuse
    /// nested <see cref="ConditionalExpression"/> with null constants inside Where
    /// clauses — a flat shape is the portable form. When the caller pulls the chain
    /// into a boolean context, <see cref="NormalizeToBool"/> rewrites this further
    /// to a pure <c>&amp;&amp;</c>-chain (no <c>IIF</c> at all).
    /// </summary>
    private static LinqExpr VisitChain(ChainExpression ch, Context ctx)
    {
        var guards = new List<LinqExpr>();
        var body = VisitChainElement((AstExpr)ch.Expression, ctx, guards);

        // Non-nullable value-type guards (Guid, int, …) can never be null — skip
        // them so we don't bloat the query with `x != null` checks the provider
        // would just constant-fold to `true` anyway.
        var realGuards = new List<LinqExpr>(guards.Count);
        foreach (var g in guards)
            if (!(g.Type.IsValueType && Nullable.GetUnderlyingType(g.Type) == null))
                realGuards.Add(g);

        if (realGuards.Count == 0)
            return body;

        var allGuardsNonNull = realGuards
            .Select(g => (LinqExpr)LinqExpr.NotEqual(g, LinqExpr.Constant(null, g.Type)))
            .Aggregate(LinqExpr.AndAlso);

        var resultType = MakeNullable(body.Type);
        if (body.Type != resultType)
            body = LinqExpr.Convert(body, resultType);

        return LinqExpr.Condition(
            allGuardsNonNull,
            body,
            LinqExpr.Constant(null, resultType));
    }

    private static LinqExpr VisitChainElement(AstExpr node, Context ctx, List<LinqExpr> guards)
    {
        switch (node)
        {
            case AstMember me:
            {
                var obj = VisitChainElement((AstExpr)me.Object, ctx, guards);
                if (me.Optional) guards.Add(obj);
                var propName = ResolveMemberName(me);
                var prop = ReflectionCache.GetProperty(obj.Type, propName)
                    ?? throw new InvalidOperationException($"Property '{propName}' not found on {obj.Type.Name}");
                return LinqExpr.Property(obj, prop);
            }
            case CallExpression ce:
            {
                if (ce.Optional)
                    throw new NotSupportedException(
                        "Optional call on a value (`x?.()`) is not supported. " +
                        "Use optional member access (`x?.method()`) instead.");
                if (ce.Callee is not AstMember callee)
                    throw new NotSupportedException("Only method calls on members supported");
                var target = VisitChainElement((AstExpr)callee.Object, ctx, guards);
                if (callee.Optional) guards.Add(target);
                return VisitCallOnTarget(target, callee, ce, ctx);
            }
            default:
                return Visit(node, ctx);
        }
    }

    private static Type MakeNullable(Type t)
        => t.IsValueType && Nullable.GetUnderlyingType(t) == null
            ? typeof(Nullable<>).MakeGenericType(t)
            : t;

    /// <summary>
    /// Recognizes <c>linq.*</c> patterns and emits a typed <see cref="ConstantExpression"/>.
    /// Literal variants (<c>linq.decimal('…')</c>, <c>linq.guid('…')</c>, …) take a string
    /// literal. Zero-arg time variants (<c>linq.today()</c>, <c>linq.now()</c>,
    /// <c>linq.utcNow()</c>) capture the current value at translation time.
    /// </summary>
    private static bool TryInterceptLinqTypedLiteral(CallExpression ce, out LinqExpr? result)
    {
        result = null;
        if (ce.Callee is not AstMember callee
            || callee.Object is not Identifier rootId || rootId.Name != "linq"
            || callee.Property is not Identifier methodId)
            return false;

        // Zero-arg "now"-style helpers — captured at translation time.
        if (ce.Arguments.Count == 0)
        {
            result = methodId.Name switch
            {
                "today"     => LinqExpr.Constant(DateTime.Today,    typeof(DateTime)),
                "now"       => LinqExpr.Constant(DateTime.Now,      typeof(DateTime)),
                "utcNow"    => LinqExpr.Constant(DateTime.UtcNow,   typeof(DateTime)),
                "todayUtc"  => LinqExpr.Constant(DateTime.UtcNow.Date, typeof(DateTime)),
                _ => null
            };
            return result != null;
        }

        if (ce.Arguments.Count != 1 || ce.Arguments[0] is not StringLiteral sl)
            throw new NotSupportedException(
                $"linq.{methodId.Name}(...) requires exactly one string literal argument, e.g. linq.{methodId.Name}('99.99').");

        var text = sl.Value;
        result = methodId.Name switch
        {
            "decimal"    => LinqExpr.Constant(LinqCasts.ParseDecimal(text),    typeof(decimal)),
            "double"     => LinqExpr.Constant(LinqCasts.ParseDouble(text),     typeof(double)),
            "int"        => LinqExpr.Constant(LinqCasts.ParseInt(text),        typeof(int)),
            "long"       => LinqExpr.Constant(LinqCasts.ParseLong(text),       typeof(long)),
            "date"       => LinqExpr.Constant(LinqCasts.ParseDate(text),       typeof(DateTime)),
            "dateUtc"    => LinqExpr.Constant(LinqCasts.ParseDateUtc(text),    typeof(DateTime)),
            "dateOffset" => LinqExpr.Constant(LinqCasts.ParseDateOffset(text), typeof(DateTimeOffset)),
            "dateOnly"   => LinqExpr.Constant(LinqCasts.ParseDateOnly(text),   typeof(DateOnly)),
            "timeOnly"   => LinqExpr.Constant(LinqCasts.ParseTimeOnly(text),   typeof(TimeOnly)),
            "timeSpan"   => LinqExpr.Constant(LinqCasts.ParseTimeSpan(text),   typeof(TimeSpan)),
            "guid"       => LinqExpr.Constant(LinqCasts.ParseGuid(text),       typeof(Guid)),
            _ => null
        };
        return result != null;
    }

    private static bool TryGetCollectionElementType(Type t, out Type? elementType)
    {
        if (t.IsArray) { elementType = t.GetElementType(); return true; }
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IEnumerable<>) ||
                def == typeof(IList<>) || def == typeof(ICollection<>))
            {
                elementType = t.GetGenericArguments()[0];
                return true;
            }
        }
        foreach (var i in t.GetInterfaces())
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            { elementType = i.GetGenericArguments()[0]; return true; }
        elementType = null;
        return false;
    }

    private static bool IsArrayMethod(string jsName) => NormalizeArrayMethod(jsName) != null;

    /// <summary>
    /// Maps a JS/TS method name (or its C# LINQ alias) on a collection to the
    /// corresponding <see cref="Enumerable"/> method. Returns <c>null</c> if the
    /// name is not recognized. Case-insensitive.
    /// </summary>
    private static string? NormalizeArrayMethod(string name) => name.ToLowerInvariant() switch
    {
        "some"    or "any"             => nameof(Enumerable.Any),
        "every"   or "all"             => nameof(Enumerable.All),
        "find"    or "firstordefault"  => nameof(Enumerable.FirstOrDefault),
        "filter"  or "where"           => nameof(Enumerable.Where),
        "map"     or "select"          => nameof(Enumerable.Select),
        "includes" or "contains"       => nameof(Enumerable.Contains),
        _ => null
    };

    private static LinqExpr TranslateArrayMethod(LinqExpr target, Type elementType, string jsMethod,
        in NodeList<AstExpr?> jsArgs, Context ctx)
    {
        var linqName = NormalizeArrayMethod(jsMethod)
            ?? throw new NotSupportedException($"Array method '{jsMethod}' not supported");

        // `includes` / `Contains` — takes a raw value, not a lambda.
        if (string.Equals(linqName, nameof(Enumerable.Contains), StringComparison.Ordinal))
        {
            if (jsArgs.Count != 1 || jsArgs[0] == null)
                throw new NotSupportedException($"{jsMethod}: expected 1 argument");
            var value = Visit(jsArgs[0]!, ctx);
            var mi = ReflectionCache.GetEnumerableContainsClosed(elementType);
            return LinqExpr.Call(mi, target, value);
        }

        // Lambda-accepting methods (Any/All/Where/Select/FirstOrDefault, incl. JS aliases).
        if (jsArgs.Count < 1 || jsArgs[0] is not ArrowFunctionExpression af)
            throw new NotSupportedException($"{jsMethod}: expected arrow function argument");

        var innerParam = BuildParameter(af, elementType);
        var innerCtx = new Context(ctx.Engine, ctx.Options);
        foreach (var p in ctx.ParamStack) innerCtx.Push(p);
        innerCtx.Push(innerParam);
        var innerBody = Visit(GetBodyExpression(af), innerCtx);

        var isSelect = string.Equals(linqName, nameof(Enumerable.Select), StringComparison.Ordinal);
        var innerReturn = isSelect ? innerBody.Type : typeof(bool);
        if (innerBody.Type != innerReturn)
            innerBody = LinqExpr.Convert(innerBody, innerReturn);

        var delegateType = typeof(Func<,>).MakeGenericType(elementType, innerReturn);
        var lambda = LinqExpr.Lambda(delegateType, innerBody, innerParam);

        var generic = ReflectionCache.GetEnumerableLambdaClosed(
            linqName, elementType, isSelect ? innerReturn : null);
        return LinqExpr.Call(generic, target, lambda);
    }

    private static LinqExpr VisitBinary(NonLogicalBinaryExpression nbe, Context ctx)
    {
        var left = Visit((AstExpr)nbe.Left, ctx);
        var right = Visit((AstExpr)nbe.Right, ctx);

        // Redundant bool-constant comparison: `x === true` → `x`, `x === false` → `!x`,
        // `x !== true` → `!x`, `x !== false` → `x`. Marten (and some other providers)
        // can't translate shapes like `StartsWith("x") == True`, but handle plain
        // `StartsWith("x")` fine — the optimization is safe semantic and broadly
        // improves provider compatibility, not just chain-related scripts.
        if (TrySimplifyBoolConstComparison(left, right, nbe.Operator, out var simplified))
            return simplified!;

        // Optional-chain shape `IIF(test, body, null) op KnownNonNullConst` flattens
        // to `test && body op KnownNonNullConst` for equality / ordering operators.
        // LINQ providers (Marten specifically) choke on any Conditional used as the
        // operand of a binary comparison — they refuse to handle the inner logical
        // test. The flattened form is strictly more translatable and semantically
        // equivalent: a null left-hand side yields `false` for these operators,
        // which is exactly what `test && …` produces when `test` is false.
        if (IsNullFlattenableComparison(nbe.Operator))
        {
            if (TryFlattenChainComparison(left, right, nbe.Operator, out var flatFromLeft))
                return flatFromLeft!;
            if (TryFlattenChainComparison(right, left, FlipSides(nbe.Operator), out var flatFromRight))
                return flatFromRight!;
        }

        (left, right) = CoerceEnumComparison(left, right);
        if (ctx.Options.CoerceNumericLiterals)
            (left, right) = CoerceNumeric(left, right);
        (left, right) = CoerceViaUserDefinedConversion(left, right);
        return nbe.Operator switch
        {
            Operator.Equality or Operator.StrictEquality     => LinqExpr.Equal(left, right),
            Operator.Inequality or Operator.StrictInequality => LinqExpr.NotEqual(left, right),
            Operator.LessThan                                => LinqExpr.LessThan(left, right),
            Operator.LessThanOrEqual                         => LinqExpr.LessThanOrEqual(left, right),
            Operator.GreaterThan                             => LinqExpr.GreaterThan(left, right),
            Operator.GreaterThanOrEqual                      => LinqExpr.GreaterThanOrEqual(left, right),
            Operator.Addition                                => LinqExpr.Add(left, right),
            Operator.Subtraction                             => LinqExpr.Subtract(left, right),
            Operator.Multiplication                          => LinqExpr.Multiply(left, right),
            Operator.Division                                => LinqExpr.Divide(left, right),
            _ => throw new NotSupportedException($"Binary operator {nbe.Operator} not supported")
        };
    }

    /// <summary>
    /// Operators for which `null op nonNullConst` evaluates to <c>false</c> in
    /// CLR lifted semantics — which matches `false && …`, so flattening is
    /// semantically safe. Excludes <c>!=</c> (null != x is <c>true</c>, needs a
    /// different lowering — not implemented).
    /// </summary>
    private static bool IsNullFlattenableComparison(Operator op) => op switch
    {
        Operator.Equality or Operator.StrictEquality => true,
        Operator.LessThan or Operator.LessThanOrEqual => true,
        Operator.GreaterThan or Operator.GreaterThanOrEqual => true,
        _ => false
    };

    private static Operator FlipSides(Operator op) => op switch
    {
        Operator.LessThan             => Operator.GreaterThan,
        Operator.LessThanOrEqual      => Operator.GreaterThanOrEqual,
        Operator.GreaterThan          => Operator.LessThan,
        Operator.GreaterThanOrEqual   => Operator.LessThanOrEqual,
        _ => op, // equality / strict-equality are symmetric
    };

    private static bool TryFlattenChainComparison(LinqExpr chainCandidate, LinqExpr other, Operator op, out LinqExpr? result)
    {
        result = null;
        if (chainCandidate is not System.Linq.Expressions.ConditionalExpression ce
            || ce.IfFalse is not ConstantExpression { Value: null })
            return false;

        if (!IsKnownNonNull(other)) return false;

        // Strip the nullable lifts so the comparison happens at the natural type —
        // keeps the SQL shape clean and matches the form Marten / EF / LINQ2DB
        // translate out of the box.
        var chainBody = UnwrapNullableLift(ce.IfTrue);
        var otherUnwrapped = UnwrapNullableLift(other);
        if (chainBody.Type != otherUnwrapped.Type)
            return false;

        var comparison = BuildComparison(chainBody, otherUnwrapped, op);
        result = LinqExpr.AndAlso(ce.Test, comparison);
        return true;
    }

    private static LinqExpr BuildComparison(LinqExpr l, LinqExpr r, Operator op)
    {
        if (TrySimplifyBoolConstComparison(l, r, op, out var simplified))
            return simplified!;
        return op switch
        {
            Operator.Equality or Operator.StrictEquality => LinqExpr.Equal(l, r),
            Operator.LessThan                            => LinqExpr.LessThan(l, r),
            Operator.LessThanOrEqual                     => LinqExpr.LessThanOrEqual(l, r),
            Operator.GreaterThan                         => LinqExpr.GreaterThan(l, r),
            Operator.GreaterThanOrEqual                  => LinqExpr.GreaterThanOrEqual(l, r),
            _ => throw new InvalidOperationException($"BuildComparison does not handle {op}")
        };
    }

    /// <summary>
    /// Collapses `boolExpr === true`, `boolExpr === false`, `boolExpr !== true`,
    /// `boolExpr !== false` (in either operand order) to the minimal equivalent
    /// <c>boolExpr</c> or <c>!boolExpr</c>. Only fires when the non-constant side
    /// is already <see cref="bool"/> — doesn't touch <see cref="Nullable{Boolean}"/>
    /// where the comparison has meaningful lifted semantics. Provider compatibility
    /// win: several LINQ providers can translate <c>StartsWith("x")</c> but not
    /// <c>StartsWith("x") == true</c>.
    /// </summary>
    private static bool TrySimplifyBoolConstComparison(LinqExpr left, LinqExpr right, Operator op, out LinqExpr? result)
    {
        result = null;
        var isEq = op is Operator.Equality or Operator.StrictEquality;
        var isNeq = op is Operator.Inequality or Operator.StrictInequality;
        if (!isEq && !isNeq) return false;

        if (TryMatchBoolConstSide(left, right, out var boolSide, out var constVal) ||
            TryMatchBoolConstSide(right, left, out boolSide, out constVal))
        {
            var wantTrue = isEq ? constVal : !constVal;
            result = wantTrue ? boolSide! : LinqExpr.Not(boolSide!);
            return true;
        }
        return false;
    }

    private static bool TryMatchBoolConstSide(LinqExpr maybeConst, LinqExpr other, out LinqExpr? boolSide, out bool constVal)
    {
        boolSide = null;
        constVal = false;
        if (maybeConst is ConstantExpression { Value: bool b } && other.Type == typeof(bool))
        {
            boolSide = other;
            constVal = b;
            return true;
        }
        return false;
    }

    private static LinqExpr UnwrapNullableLift(LinqExpr e)
        => e is System.Linq.Expressions.UnaryExpression { NodeType: ExpressionType.Convert } u
           && Nullable.GetUnderlyingType(u.Type) is Type underlying
           && underlying == u.Operand.Type
            ? u.Operand
            : e;

    private static bool IsKnownNonNull(LinqExpr e)
        => UnwrapNullableLift(e) is ConstantExpression { Value: not null };

    private static LinqExpr VisitLogical(LogicalExpression le, Context ctx)
    {
        if (le.Operator == Operator.LogicalAnd)
        {
            var left = Visit((AstExpr)le.Left, ctx);

            // Collect narrowing frames from the left operand so the right operand can
            // resolve subtype-only members via TryResolveViaIntersection.
            // Two sources: TypeIs nodes (CLR-type mappings) and Equal(p.Prop, "val") nodes
            // from combined discriminator mappings that have both PropertyName and ConcreteType.
            var narrowings = new Dictionary<ParameterExpression, List<Type>>();
            CollectNarrowings(left, narrowings, ctx.Options.DiscriminatorMappings);
            foreach (var (param, types) in narrowings)
                ctx.PushNarrowing(param, types);

            var right = Visit((AstExpr)le.Right, ctx);

            foreach (var (param, _) in narrowings)
                ctx.PopNarrowing(param);

            return LinqExpr.AndAlso(NormalizeToBool(left), NormalizeToBool(right));
        }

        var leftExpr = Visit((AstExpr)le.Left, ctx);
        var rightExpr = Visit((AstExpr)le.Right, ctx);
        return le.Operator switch
        {
            Operator.LogicalOr         => LinqExpr.OrElse(NormalizeToBool(leftExpr), NormalizeToBool(rightExpr)),
            Operator.NullishCoalescing => BuildCoalesce(leftExpr, rightExpr),
            _ => throw new NotSupportedException($"Logical operator {le.Operator} not supported")
        };
    }

    /// <summary>
    /// Recursively collects narrowing frames reachable via <c>AndAlso</c>/<c>OrElse</c>
    /// sub-trees. Two node types contribute a frame:
    /// <list type="bullet">
    ///   <item><c>TypeIs(param, T)</c> — CLR-type mappings.</item>
    ///   <item><c>param.Prop == "value"</c> — combined discriminator mappings where
    ///   both <c>PropertyName</c> (for ORM-safe LINQ) and <c>ConcreteType</c> (for
    ///   narrowing) are set. The <c>ConcreteType</c> is injected as the narrowing target.</item>
    /// </list>
    /// </summary>
    private static void CollectNarrowings(
        LinqExpr expr,
        Dictionary<ParameterExpression, List<Type>> result,
        IReadOnlyList<DiscriminatorMapping> mappings)
    {
        switch (expr)
        {
            case TypeBinaryExpression { NodeType: ExpressionType.TypeIs } tb
                when tb.Expression is ParameterExpression p:
                if (!result.TryGetValue(p, out var list)) result[p] = list = [];
                if (!list.Contains(tb.TypeOperand)) list.Add(tb.TypeOperand);
                break;
            case System.Linq.Expressions.BinaryExpression { NodeType: ExpressionType.Equal } eq
                when eq.Left is System.Linq.Expressions.MemberExpression { Expression: ParameterExpression ep } me
                && eq.Right is ConstantExpression { Value: string val }:
            {
                var m = mappings.FirstOrDefault(x =>
                    x.PropertyName == me.Member.Name &&
                    x.Value == val &&
                    x.ConcreteType != null &&
                    x.BaseType.IsAssignableFrom(ep.Type));
                if (m != null)
                {
                    if (!result.TryGetValue(ep, out var mlist)) result[ep] = mlist = [];
                    if (!mlist.Contains(m.ConcreteType!)) mlist.Add(m.ConcreteType!);
                }
                break;
            }
            case System.Linq.Expressions.BinaryExpression
                { NodeType: ExpressionType.AndAlso or ExpressionType.OrElse } bin:
                CollectNarrowings(bin.Left, result, mappings);
                CollectNarrowings(bin.Right, result, mappings);
                break;
        }
    }

    /// <summary>
    /// Builds a <c>??</c> expression. <see cref="LinqExpr.Coalesce"/> requires the
    /// left side to be a reference type or <see cref="Nullable{T}"/>; if a non-nullable
    /// value-type left-hand side slips through (typically impossible from JS source,
    /// but possible through closure-bound values), the expression reduces to
    /// <c>left</c> — it can never be null.
    /// </summary>
    private static LinqExpr BuildCoalesce(LinqExpr left, LinqExpr right)
    {
        if (left.Type.IsValueType && Nullable.GetUnderlyingType(left.Type) == null)
            return left;
        if (left.Type != right.Type)
        {
            var leftNonNull = Nullable.GetUnderlyingType(left.Type) ?? left.Type;
            if (right.Type == leftNonNull)
                return LinqExpr.Coalesce(left, right);
            if (leftNonNull.IsAssignableFrom(right.Type))
                return LinqExpr.Coalesce(left, LinqExpr.Convert(right, leftNonNull));
        }
        return LinqExpr.Coalesce(left, right);
    }

    private static LinqExpr VisitUnary(AstUnary ue, Context ctx)
    {
        var operand = Visit((AstExpr)ue.Argument, ctx);
        return ue.Operator switch
        {
            Operator.LogicalNot    => LinqExpr.Not(NormalizeToBool(operand)),
            Operator.UnaryNegation => NegateOrFold(operand),
            Operator.UnaryPlus     => operand,
            _ => throw new NotSupportedException($"Unary operator {ue.Operator} not supported")
        };
    }

    /// <summary>
    /// Constant-folds unary negation on numeric literals (JS <c>-7</c> arrives as
    /// <c>Unary(-, Constant(7))</c>; C# source would compile to <c>Constant(-7)</c>).
    /// LINQ providers like Marten reject the Unary form but accept the folded constant.
    /// </summary>
    private static LinqExpr NegateOrFold(LinqExpr operand)
    {
        if (operand is ConstantExpression { Value: not null } ce && IsNumeric(ce.Type))
        {
            object? negated = ce.Value switch
            {
                int i     => -i,
                long l    => -l,
                double d  => -d,
                decimal m => -m,
                float f   => -f,
                short s   => -s,
                _ => null
            };
            if (negated != null)
                return LinqExpr.Constant(negated, ce.Type);
        }
        return LinqExpr.Negate(operand);
    }

    private static LinqExpr VisitConditional(AstConditional cond, Context ctx)
    {
        var test = NormalizeToBool(Visit((AstExpr)cond.Test, ctx));
        var ifTrue = Visit((AstExpr)cond.Consequent, ctx);
        var ifFalse = Visit((AstExpr)cond.Alternate, ctx);
        return LinqExpr.Condition(test, ifTrue, ifFalse);
    }

    /// <summary>
    /// Converts a <see cref="Nullable{Boolean}"/> expression into a <see cref="bool"/>
    /// with JS-truthy semantics: <c>null</c> is treated as <c>false</c>. Non-nullable
    /// <c>bool</c> is passed through. Used at every boolean-context site (lambda
    /// body, <c>!</c>, <c>&amp;&amp;</c>, <c>||</c>, ternary test) so optional
    /// chaining inside those contexts behaves the same as in JS — <c>null/undefined
    /// → false</c> — without requiring an explicit <c>=== true</c> in the source.
    ///
    /// Special-cases the optional-chain shape
    /// <c>Condition(test, Convert(boolBody, bool?), Constant(null))</c> emitted by
    /// <see cref="VisitChain"/>: rewrites it to <c>test &amp;&amp; boolBody</c> — a
    /// pure bool expression with zero <c>IIF</c> nodes. This is what a C# author
    /// would hand-write for a null-safe predicate, and — critically — it's the only
    /// form Marten's <c>WhereClauseParser</c> reliably translates (nested
    /// <see cref="ConditionalExpression"/> with <c>null</c> constants throws there).
    /// The generic fallback emits <c>x == true</c>, byte-identical to the C#
    /// compiler's output for an explicit <c>== true</c> on a <c>bool?</c>.
    /// </summary>
    private static LinqExpr NormalizeToBool(LinqExpr e)
    {
        if (e.Type != typeof(bool?)) return e;

        if (e is System.Linq.Expressions.ConditionalExpression { IfFalse: ConstantExpression { Value: null } } ce)
        {
            var body = UnwrapBoolLift(ce.IfTrue);
            if (body.Type == typeof(bool))
                return LinqExpr.AndAlso(ce.Test, body);
        }

        return LinqExpr.Equal(e, LinqExpr.Constant(true, typeof(bool?)));
    }

    /// <summary>
    /// If <paramref name="e"/> is <c>Convert(x, Nullable&lt;bool&gt;)</c> where
    /// <c>x</c> is already <c>bool</c>, returns <c>x</c> unwrapped. Otherwise
    /// returns <paramref name="e"/> as-is. Used to peel the lift that
    /// <see cref="VisitChain"/> applies so boolean bodies re-surface as plain
    /// <c>bool</c> in the rewritten expression.
    /// </summary>
    private static LinqExpr UnwrapBoolLift(LinqExpr e)
        => e is System.Linq.Expressions.UnaryExpression { NodeType: ExpressionType.Convert } u
           && u.Type == typeof(bool?) && u.Operand.Type == typeof(bool)
            ? u.Operand
            : e;

    /// <summary>
    /// When a binary expression compares an enum property to a string literal
    /// (<c>u.Status === 'Active'</c>) or a numeric literal (<c>u.Status === 1</c>),
    /// convert the literal side into a typed enum <see cref="ConstantExpression"/>.
    /// Provider-agnostic: leaves the storage-format decision (int vs string) to
    /// the LINQ provider's own mapping.
    /// </summary>
    private static (LinqExpr left, LinqExpr right) CoerceEnumComparison(LinqExpr left, LinqExpr right)
    {
        if (left.Type == right.Type) return (left, right);

        if (left.Type.IsEnum && TryConvertConstantToEnum(right, left.Type, out var convertedRight))
            return (left, convertedRight!);

        if (right.Type.IsEnum && TryConvertConstantToEnum(left, right.Type, out var convertedLeft))
            return (convertedLeft!, right);

        return (left, right);
    }

    private static bool TryConvertConstantToEnum(LinqExpr source, Type enumType, out LinqExpr? converted)
    {
        converted = null;
        if (source is not ConstantExpression ce) return false;

        // String literal: parse by member name.
        if (ce.Type == typeof(string) && ce.Value is string s)
        {
            object parsed;
            try { parsed = Enum.Parse(enumType, s, ignoreCase: true); }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"Cannot convert string '{s}' to enum {enumType.Name}: no matching member (case-insensitive). " +
                    $"Valid values: {string.Join(", ", Enum.GetNames(enumType))}.", ex);
            }
            converted = LinqExpr.Constant(parsed, enumType);
            return true;
        }

        // Numeric literal: convert via the enum's underlying type (typically int).
        if (ce.Value is IConvertible && IsNumeric(ce.Type))
        {
            var underlying = Convert.ChangeType(ce.Value, Enum.GetUnderlyingType(enumType),
                System.Globalization.CultureInfo.InvariantCulture);
            converted = LinqExpr.Constant(Enum.ToObject(enumType, underlying!), enumType);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Applies user-defined <c>implicit</c> operator conversions when the two
    /// sides of a binary op don't match type. Enables wrappers like
    /// <c>CsDateTime</c> (with <c>implicit operator DateTime</c>) to be compared
    /// against columns of the underlying type transparently.
    /// </summary>
    private static (LinqExpr left, LinqExpr right) CoerceViaUserDefinedConversion(LinqExpr left, LinqExpr right)
    {
        if (left.Type == right.Type) return (left, right);

        // Prefer converting the non-root (constant/call) side. In LINQ predicates
        // the MemberExpression side is almost always rooted at the parameter,
        // so we try right→left first, then left→right.
        var rToL = ReflectionCache.GetImplicitCastMethod(right.Type, left.Type);
        if (rToL != null)
            return (left, LinqExpr.Convert(right, left.Type, rToL));

        var lToR = ReflectionCache.GetImplicitCastMethod(left.Type, right.Type);
        if (lToR != null)
            return (LinqExpr.Convert(left, right.Type, lToR), right);

        return (left, right);
    }

    private static (LinqExpr left, LinqExpr right) CoerceNumeric(LinqExpr left, LinqExpr right)
    {
        if (left.Type == right.Type) return (left, right);
        if (right is ConstantExpression rc && IsNumeric(left.Type) && IsNumeric(right.Type))
            return (left, LinqExpr.Constant(Convert.ChangeType(rc.Value, left.Type), left.Type));
        if (left is ConstantExpression lc && IsNumeric(left.Type) && IsNumeric(right.Type))
            return (LinqExpr.Constant(Convert.ChangeType(lc.Value, right.Type), right.Type), right);
        return (left, right);
    }

    private static bool IsNumeric(Type t) =>
        t == typeof(int) || t == typeof(long) || t == typeof(double) ||
        t == typeof(decimal) || t == typeof(float) || t == typeof(short) || t == typeof(byte);

    /// <summary>
    /// Resolves a discriminator string value to a LINQ expression node for the given parameter.
    /// For CLR-type mappings produces <c>TypeIs(param, ConcreteType)</c>;
    /// for property-based mappings produces <c>param.PropertyName == value</c>.
    /// Lookup order: explicit DiscriminatorMapping → TypeAliases → namespace-mapped names.
    /// </summary>
    private static bool TryResolveDiscriminatorNode(
        string value, ParameterExpression param, Context ctx, out LinqExpr? node)
    {
        var mapping = ctx.Options.DiscriminatorMappings
            .FirstOrDefault(m => m.BaseType.IsAssignableFrom(param.Type) && m.Value == value);
        if (mapping != null)
        {
            node = mapping.PropertyName != null
                ? LinqExpr.Equal(LinqExpr.Property(param, mapping.PropertyName), LinqExpr.Constant(value))
                : LinqExpr.TypeIs(param, mapping.ConcreteType!);
            return true;
        }

        // TypeAlias / namespace-mapping fallbacks always produce a CLR TypeIs check.
        if (ctx.Options.TypeAliases.TryGetValue(value, out var aliasedType))
        { node = LinqExpr.TypeIs(param, aliasedType); return true; }

        var namespaceMapped = ctx.GetNamespaceMapped();
        if (namespaceMapped.TryGetValue(value, out var nsMappedType))
        { node = LinqExpr.TypeIs(param, nsMappedType); return true; }

        node = null;
        return false;
    }

    /// <summary>
    /// Intercepts <c>Type.Is(param, 'value')</c>.
    /// CLR-type mapping → <c>TypeIs(param, ConcreteType)</c>.
    /// Property-based mapping → <c>param.PropertyName == "value"</c>.
    /// Falls through when no match is found.
    /// </summary>
    private static bool TryInterceptDiscriminatorIs(
        CallExpression ce, AstMember callee, Context ctx, out LinqExpr? result)
    {
        result = null;
        if (callee.Object is not Identifier { Name: "Type" }) return false;
        if (callee.Property is not Identifier { Name: "Is" }) return false;
        if (ce.Arguments.Count != 2) return false;
        if (ce.Arguments[0] is not Identifier paramId) return false;
        if (ce.Arguments[1] is not StringLiteral sl) return false;
        var param = ctx.Find(paramId.Name);
        if (param == null) return false;

        if (!TryResolveDiscriminatorNode(sl.Value, param, ctx, out var node))
            return false;

        result = node;
        return true;
    }

    /// <summary>
    /// Intercepts <c>Type.IsOneOf(param, ['v1','v2',…])</c>, expanding it to an
    /// <c>OrElse</c> chain — one node per value, each resolved via
    /// <see cref="TryResolveDiscriminatorNode"/> (CLR-type or property-based).
    /// AND-narrowing on the right side of <c>&amp;&amp;</c> works automatically because
    /// <c>CollectNarrowings</c> already traverses <c>OrElse</c> trees.
    /// Throws when any element is not a string literal or has no matching mapping.
    /// </summary>
    private static bool TryInterceptDiscriminatorIsOneOf(
        CallExpression ce, AstMember callee, Context ctx, out LinqExpr? result)
    {
        result = null;
        if (callee.Object is not Identifier { Name: "Type" }) return false;
        if (callee.Property is not Identifier { Name: "IsOneOf" }) return false;
        if (ce.Arguments.Count != 2) return false;
        if (ce.Arguments[0] is not Identifier paramId) return false;
        if (ce.Arguments[1] is not ArrayExpression arr) return false;
        var param = ctx.Find(paramId.Name);
        if (param == null) return false;

        var nodes = new List<LinqExpr>();
        foreach (var element in arr.Elements)
        {
            if (element is not StringLiteral sl)
                throw new InvalidOperationException(
                    "Type.IsOneOf: all array elements must be string literals.");
            if (!TryResolveDiscriminatorNode(sl.Value, param, ctx, out var node))
                throw new InvalidOperationException(
                    $"Type.IsOneOf: no discriminator mapping found for '{sl.Value}' on '{param.Type.Name}'.");
            nodes.Add(node!);
        }

        if (nodes.Count == 0)
            throw new InvalidOperationException("Type.IsOneOf: array must not be empty.");

        result = nodes.Skip(1).Aggregate(nodes[0], LinqExpr.OrElse);
        return true;
    }
}
