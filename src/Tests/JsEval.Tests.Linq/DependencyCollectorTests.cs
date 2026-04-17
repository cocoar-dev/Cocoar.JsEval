using System.Linq.Expressions;
using Cocoar.JsEval.Linq.Dependencies;
using Xunit;

namespace Cocoar.JsEval.Tests.Linq;

public class DependencyCollectorTests
{
    [Fact]
    public void Collect_SingleProperty()
    {
        Expression<Func<TestUser, bool>> e = u => u.Name == "x";
        var deps = ExpressionDependencyCollector.Collect(e);
        Assert.Equal(new[] { "Name" }, deps.Paths.OrderBy(p => p));
        Assert.False(deps.Unsafe);
    }

    [Fact]
    public void Collect_NestedProperty_RecordsBothSegments()
    {
        Expression<Func<TestUser, bool>> e = u => u.Address!.City == "Vienna";
        var deps = ExpressionDependencyCollector.Collect(e);
        Assert.Contains("Address", deps.Paths);
        Assert.Contains("Address.City", deps.Paths);
    }

    [Fact]
    public void Collect_MultipleProperties()
    {
        Expression<Func<TestUser, bool>> e = u =>
            u.Name.StartsWith("A") && u.IsActive && u.Age > 18;
        var deps = ExpressionDependencyCollector.Collect(e);
        Assert.Equal(new[] { "Age", "IsActive", "Name" }, deps.Paths.OrderBy(p => p));
    }

    [Fact]
    public void Collect_ArrayPredicate_InnerParamNotTreatedAsRootProperty()
    {
        Expression<Func<TestUser, bool>> e = u => u.Tags.Any(t => t == "vip");
        var deps = ExpressionDependencyCollector.Collect(e);
        // "Tags" is recorded (root property), but "t" (inner param) is not.
        Assert.Contains("Tags", deps.Paths);
        Assert.DoesNotContain(deps.Paths, p => p.StartsWith("t"));
    }

    [Fact]
    public void Collect_TopLevel_ExtractsRootSegments()
    {
        Expression<Func<TestUser, bool>> e = u =>
            u.Name == "x" && u.Address!.City == "Vienna" && u.Tags.Any(t => t == "v");
        var deps = ExpressionDependencyCollector.Collect(e);
        Assert.Equal(new[] { "Address", "Name", "Tags" }, deps.TopLevel.OrderBy(x => x));
    }

    [Fact]
    public void DependsOn_DirectHit()
    {
        Expression<Func<TestUser, bool>> e = u => u.Name == "x";
        var deps = ExpressionDependencyCollector.Collect(e);
        Assert.True(deps.DependsOn("Name"));
        Assert.False(deps.DependsOn("Email"));
    }

    [Fact]
    public void DependsOn_AncestorPath_Matches()
    {
        // Expression touches only "Address.City", but when "Address" itself is
        // replaced (parent changed), the dependency is still considered affected.
        Expression<Func<TestUser, bool>> e = u => u.Address!.City == "Vienna";
        var deps = ExpressionDependencyCollector.Collect(e);
        Assert.True(deps.DependsOn("Address.City"));
        Assert.True(deps.DependsOn("Address.Zip"), "Changing a sibling under the same parent ancestor should flag.");
    }

    [Fact]
    public void DependsOn_UnrelatedProperty_DoesNotMatch()
    {
        Expression<Func<TestUser, bool>> e = u => u.Name == "x";
        var deps = ExpressionDependencyCollector.Collect(e);
        Assert.False(deps.DependsOn("Email"));
        Assert.False(deps.DependsOn("Age"));
    }

    [Fact]
    public void DependsOn_UnsafeFlag_ReturnsTrueForEverything()
    {
        var deps = new PropertyDependencies(new HashSet<string>(), isUnsafe: true);
        Assert.True(deps.DependsOn("Anything"));
        Assert.True(deps.DependsOn("Nested.Path"));
    }

    [Fact]
    public void ClosureConstant_IsNotTracked_AsRootDependency()
    {
        // prefix is captured closure -> ConstantExpression in the tree, not a MemberExpression
        // on the root parameter. So it must not appear as a dependency.
        string prefix = "A";
        Expression<Func<TestUser, bool>> e = u => u.Name.StartsWith(prefix);
        var deps = ExpressionDependencyCollector.Collect(e);
        Assert.Contains("Name", deps.Paths);
        Assert.DoesNotContain("prefix", deps.Paths);
    }
}
