using Cocoar.JsEval.Linq;
using Jint;
using Xunit;

namespace Cocoar.JsEval.Tests.Linq;

/// <summary>
/// End-to-end tests on in-memory queryables. Proves that a JS lambda routed
/// through the registered extension methods produces correct filtering —
/// without depending on any specific LINQ provider.
/// </summary>
public class JsLinqExtensionsTests
{
    private static readonly List<TestUser> _sample =
    [
        new() { Name = "Alice",   IsActive = true,  Age = 30, Tags = ["vip"]  },
        new() { Name = "Andrew",  IsActive = false, Age = 42, Tags = ["new"]  },
        new() { Name = "Bob",     IsActive = true,  Age = 25, Tags = ["vip"]  },
        new() { Name = "Charlie", IsActive = true,  Age = 50, Tags = []       },
    ];

    private static Jint.Engine BuildEngine()
    {
        var engine = new Jint.Engine(opts =>
        {
            opts.AllowClr(typeof(TestUser).Assembly);
            opts.AddExtensionMethods(typeof(JsLinqExtensions));
        });
        engine.SetValue("users", _sample.AsQueryable());
        return engine;
    }

    [Fact]
    public void Where_FiltersInMemoryQueryable()
    {
        var engine = BuildEngine();
        using (JsLinqContext.Scope(engine))
        {
            var result = engine.Evaluate("users.where(u => u.Name.startsWith('A') && u.IsActive)");
            var filtered = ((IQueryable<TestUser>)result.ToObject()!).ToList();
            Assert.Single(filtered);
            Assert.Equal("Alice", filtered[0].Name);
        }
    }

    [Fact]
    public void Count_WithPredicate()
    {
        var engine = BuildEngine();
        using (JsLinqContext.Scope(engine))
        {
            var count = (int)engine.Evaluate("users.count(u => u.IsActive)").AsNumber();
            Assert.Equal(3, count);
        }
    }

    [Fact]
    public void Count_WithNullPredicate_EquivalentToNoPredicate()
    {
        // JS users.count() without args trips Jint's overload resolution in specific
        // environments; pass an explicit null to hit the 2-arg overload cleanly.
        var engine = BuildEngine();
        using (JsLinqContext.Scope(engine))
        {
            var count = (int)engine.Evaluate("users.count(null)").AsNumber();
            Assert.Equal(4, count);
        }
    }

    [Fact]
    public void Find_ReturnsFirstMatch()
    {
        var engine = BuildEngine();
        using (JsLinqContext.Scope(engine))
        {
            var bob = (TestUser?)engine.Evaluate("users.find(u => u.Name === 'Bob')").ToObject();
            Assert.NotNull(bob);
            Assert.Equal(25, bob!.Age);
        }
    }

    [Fact]
    public void Any_WithPredicate()
    {
        var engine = BuildEngine();
        using (JsLinqContext.Scope(engine))
        {
            Assert.True(engine.Evaluate("users.any(u => u.Age > 40)").AsBoolean());
            Assert.False(engine.Evaluate("users.any(u => u.Age > 100)").AsBoolean());
        }
    }

    [Fact]
    public void Where_Chains_WithoutMaterializing()
    {
        var engine = BuildEngine();
        using (JsLinqContext.Scope(engine))
        {
            var chained = engine.Evaluate(
                "users.where(u => u.IsActive).where(u => u.Age < 40)");
            var filtered = ((IQueryable<TestUser>)chained.ToObject()!).ToList();
            Assert.Equal(2, filtered.Count);
            Assert.Contains(filtered, u => u.Name == "Alice");
            Assert.Contains(filtered, u => u.Name == "Bob");
        }
    }

    [Fact]
    public void OrderBy_Ascending_StringKey()
    {
        var engine = BuildEngine();
        using (JsLinqContext.Scope(engine))
        {
            var ordered = engine.Evaluate("users.orderBy(u => u.Name)");
            var list = ((IQueryable<TestUser>)ordered.ToObject()!).ToList();
            Assert.Equal(new[] { "Alice", "Andrew", "Bob", "Charlie" },
                list.Select(u => u.Name));
        }
    }

    [Fact]
    public void OrderByDescending_NumericKey()
    {
        var engine = BuildEngine();
        using (JsLinqContext.Scope(engine))
        {
            var ordered = engine.Evaluate("users.orderByDescending(u => u.Age)");
            var list = ((IQueryable<TestUser>)ordered.ToObject()!).ToList();
            Assert.Equal(new[] { 50, 42, 30, 25 }, list.Select(u => u.Age));
        }
    }

    [Fact]
    public void OrderBy_Then_ThenByDescending_TwoKeys()
    {
        // Data: 4 users; IsActive splits into {Alice,Bob,Charlie} vs {Andrew}.
        // Then inside each IsActive group, descending by Age.
        var engine = BuildEngine();
        using (JsLinqContext.Scope(engine))
        {
            var ordered = engine.Evaluate(
                "users.orderBy(u => u.IsActive).thenByDescending(u => u.Age)");
            var list = ((IQueryable<TestUser>)ordered.ToObject()!).ToList();
            // IsActive=false first (Andrew), then IsActive=true group sorted by Age desc.
            Assert.Equal(
                new[] { "Andrew", "Charlie", "Alice", "Bob" },
                list.Select(u => u.Name));
        }
    }

    [Fact]
    public void OrderBy_Chains_WithWhere()
    {
        var engine = BuildEngine();
        using (JsLinqContext.Scope(engine))
        {
            var result = engine.Evaluate(
                "users.where(u => u.IsActive).orderBy(u => u.Age)");
            var list = ((IQueryable<TestUser>)result.ToObject()!).ToList();
            Assert.Equal(new[] { "Bob", "Alice", "Charlie" }, list.Select(u => u.Name));
        }
    }
}
