using System.Linq.Expressions;

namespace Cocoar.JsEval.Linq.Dependencies;

/// <summary>
/// Walks an Expression tree and collects all property paths accessed on the
/// root parameter(s). Use the result to decide whether a query needs to be
/// re-executed after a given property changed.
/// </summary>
public static class ExpressionDependencyCollector
{
    /// <summary>Collects property dependencies for a typed lambda.</summary>
    public static PropertyDependencies Collect<T>(Expression<Func<T, bool>> expression) =>
        CollectCore(expression);

    /// <summary>Generic collect: works with any lambda (useful when type is only known at runtime).</summary>
    public static PropertyDependencies Collect(LambdaExpression expression) =>
        CollectCore(expression);

    private static PropertyDependencies CollectCore(LambdaExpression expression)
    {
        var visitor = new Visitor(expression.Parameters);
        visitor.Visit(expression.Body);
        return new PropertyDependencies(visitor.Paths, visitor.IsUnsafe);
    }

    private sealed class Visitor : ExpressionVisitor
    {
        public readonly HashSet<string> Paths = new(StringComparer.Ordinal);
        public bool IsUnsafe { get; private set; }
        private readonly HashSet<ParameterExpression> _roots;

        public Visitor(IEnumerable<ParameterExpression> roots) => _roots = new(roots);

        protected override Expression VisitMember(MemberExpression node)
        {
            if (TryExtractRootedPath(node, out var path))
                Paths.Add(path);
            return base.VisitMember(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // If the method call is dynamic/unsupported in a way we cannot analyze,
            // we conservatively flag unsafe. For now: no special handling — walk normally.
            return base.VisitMethodCall(node);
        }

        protected override Expression VisitIndex(IndexExpression node)
        {
            // Dynamic indexers on the root -> invalidate-all.
            if (node.Object is MemberExpression me && IsRooted(me))
                IsUnsafe = true;
            return base.VisitIndex(node);
        }

        private bool IsRooted(MemberExpression me)
        {
            Expression? cur = me;
            while (cur is MemberExpression inner) cur = inner.Expression;
            return cur is ParameterExpression p && _roots.Contains(p);
        }

        private bool TryExtractRootedPath(MemberExpression me, out string path)
        {
            var stack = new Stack<string>();
            Expression? cur = me;
            while (cur is MemberExpression inner)
            {
                stack.Push(inner.Member.Name);
                cur = inner.Expression;
            }
            if (cur is ParameterExpression p && _roots.Contains(p))
            {
                path = string.Join('.', stack);
                return true;
            }
            path = string.Empty;
            return false;
        }
    }
}
