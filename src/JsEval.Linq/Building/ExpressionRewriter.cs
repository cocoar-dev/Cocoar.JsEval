using System.Linq.Expressions;

namespace Cocoar.JsEval.Linq.Building;

/// <summary>
/// Rewrites Expression Trees to fix common incompatibilities with LINQ providers.
/// Use this when you receive an expression from C# compiler or other sources
/// that needs adjustments for your ORM/database.
/// </summary>
internal sealed class ExpressionRewriter : ExpressionVisitor
{
    /// <summary>
    /// Rewrites enum comparisons that use <c>Convert(enum, Int32)</c> to compare
    /// the enum values directly (without integer conversion).
    ///
    /// This fixes the common problem where ORMs that store enums as strings
    /// generate <c>CAST(column AS integer) = 2</c> instead of <c>column = 'Active'</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// // Before: t => (Convert(t.Status, Int32) == Convert(value, Int32))
    /// var fixed = ExpressionRewriter.RewriteEnumConversions(expression);
    /// // After:  t => (t.Status == value)
    /// </code>
    /// </example>
    public static Expression<Func<T, bool>> RewriteEnumConversions<T>(Expression<Func<T, bool>> expression)
    {
        var rewriter = new EnumConversionRewriter();
        var rewritten = rewriter.Visit(expression.Body);
        return Expression.Lambda<Func<T, bool>>(rewritten, expression.Parameters);
    }

    private sealed class EnumConversionRewriter : ExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
            {
                var left = UnwrapEnumConvert(node.Left);
                var right = UnwrapEnumConvert(node.Right);

                if (left != node.Left || right != node.Right)
                {
                    if (left.Type != right.Type && left.Type.IsEnum && right.Type == typeof(int))
                    {
                        if (right is ConstantExpression constExpr && constExpr.Value is int intValue)
                            right = Expression.Constant(Enum.ToObject(left.Type, intValue), left.Type);
                    }
                    else if (right.Type != left.Type && right.Type.IsEnum && left.Type == typeof(int))
                    {
                        if (left is ConstantExpression constExpr && constExpr.Value is int intValue)
                            left = Expression.Constant(Enum.ToObject(right.Type, intValue), right.Type);
                    }

                    return node.NodeType == ExpressionType.Equal
                        ? Expression.Equal(left, right)
                        : Expression.NotEqual(left, right);
                }
            }
            return base.VisitBinary(node);
        }

        private static Expression UnwrapEnumConvert(Expression expression)
        {
            if (expression is UnaryExpression { NodeType: ExpressionType.Convert } unary
                && unary.Operand.Type.IsEnum
                && (unary.Type == typeof(int) || unary.Type == typeof(long)))
            {
                return unary.Operand;
            }
            return expression;
        }
    }
}
