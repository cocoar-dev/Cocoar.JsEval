using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Cocoar.JsEval.Expressions;
using Xunit;

namespace JsEval.Tests.Expressions;

// --- Test domain ---

public enum TodoStatus { None, New, InProgress, Done, Archived }

public class CustomerView
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}

public class ResponsibleView
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public class TodoView
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public TodoStatus Status { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ResponsibleId { get; set; }
    public bool IsArchived { get; set; }
    public DateTime DueDate { get; set; }
    public CustomerView? Customer { get; set; }
    public List<ResponsibleView> Responsibles { get; set; } = [];
}

// --- Tests ---

public class ListHolderTests
{
    [Fact]
    public void Create_WrapsList()
    {
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var holder = ListHolder.Create(ids);

        Assert.Equal(2, holder.Values.Count);
    }

    [Fact]
    public void AsExpression_ReturnsMemberExpression()
    {
        var holder = ListHolder.Create(new[] { 1, 2, 3 });
        var expr = holder.AsExpression();

        Assert.IsAssignableFrom<MemberExpression>(expr);
        Assert.Equal("Values", expr.Member.Name);
    }

    [Fact]
    public void Contains_WithListHolder_WorksInLinq()
    {
        var allowedIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var todos = new List<TodoView>
        {
            new() { Id = Guid.NewGuid(), CustomerId = allowedIds[0] },
            new() { Id = Guid.NewGuid(), CustomerId = allowedIds[1] },
            new() { Id = Guid.NewGuid(), CustomerId = Guid.NewGuid() },
        };

        var expr = ExpressionHelper.Contains<TodoView, Guid>("CustomerId", allowedIds);
        var filtered = todos.AsQueryable().Where(expr).ToList();

        Assert.Equal(2, filtered.Count);
    }
}

public class ExpressionHelperTests
{
    private static readonly List<TodoView> TestData =
    [
        new() { Id = Guid.NewGuid(), Title = "Alpha Task", Status = TodoStatus.New, IsArchived = false, DueDate = new DateTime(2025, 6, 1) },
        new() { Id = Guid.NewGuid(), Title = "Beta Task", Status = TodoStatus.InProgress, IsArchived = false, DueDate = new DateTime(2025, 7, 1) },
        new() { Id = Guid.NewGuid(), Title = "Gamma Done", Status = TodoStatus.Done, IsArchived = true, DueDate = new DateTime(2025, 1, 1) },
    ];

    [Fact]
    public void Equal_FiltersCorrectly()
    {
        var expr = ExpressionHelper.Equal<TodoView, bool>("IsArchived", false);
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void NotEqual_FiltersCorrectly()
    {
        var expr = ExpressionHelper.NotEqual<TodoView, TodoStatus>("Status", TodoStatus.Done);
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void StartsWith_FiltersCorrectly()
    {
        var expr = ExpressionHelper.StartsWith<TodoView>("Title", "Alpha");
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Single(result);
        Assert.Equal("Alpha Task", result[0].Title);
    }

    [Fact]
    public void StringContains_FiltersCorrectly()
    {
        var expr = ExpressionHelper.StringContains<TodoView>("Title", "Task");
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GreaterThan_FiltersCorrectly()
    {
        var expr = ExpressionHelper.GreaterThan<TodoView, DateTime>("DueDate", new DateTime(2025, 6, 15));
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Single(result);
        Assert.Equal("Beta Task", result[0].Title);
    }

    [Fact]
    public void LessThan_FiltersCorrectly()
    {
        var expr = ExpressionHelper.LessThan<TodoView, DateTime>("DueDate", new DateTime(2025, 2, 1));
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Single(result);
        Assert.Equal("Gamma Done", result[0].Title);
    }

    [Fact]
    public void And_CombinesExpressions()
    {
        var notArchived = ExpressionHelper.Equal<TodoView, bool>("IsArchived", false);
        var isNew = ExpressionHelper.Equal<TodoView, TodoStatus>("Status", TodoStatus.New);
        var combined = ExpressionHelper.And(notArchived, isNew);

        var result = TestData.AsQueryable().Where(combined).ToList();

        Assert.Single(result);
        Assert.Equal("Alpha Task", result[0].Title);
    }

    [Fact]
    public void Or_CombinesExpressions()
    {
        var isNew = ExpressionHelper.Equal<TodoView, TodoStatus>("Status", TodoStatus.New);
        var isDone = ExpressionHelper.Equal<TodoView, TodoStatus>("Status", TodoStatus.Done);
        var combined = ExpressionHelper.Or(isNew, isDone);

        var result = TestData.AsQueryable().Where(combined).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Not_NegatesExpression()
    {
        var isArchived = ExpressionHelper.Equal<TodoView, bool>("IsArchived", true);
        var notArchived = ExpressionHelper.Not(isArchived);

        var result = TestData.AsQueryable().Where(notArchived).ToList();

        Assert.Equal(2, result.Count);
    }
}

public class EnumExpressionHelperTests
{
    private static readonly List<TodoView> TestData =
    [
        new() { Status = TodoStatus.New },
        new() { Status = TodoStatus.InProgress },
        new() { Status = TodoStatus.Done },
    ];

    [Fact]
    public void Equals_GenericEnum_FiltersCorrectly()
    {
        var expr = EnumExpressionHelper.Equals<TodoView, TodoStatus>("Status", TodoStatus.InProgress);
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Single(result);
        Assert.Equal(TodoStatus.InProgress, result[0].Status);
    }

    [Fact]
    public void Equals_FromString_FiltersCorrectly()
    {
        var expr = EnumExpressionHelper.Equals<TodoView>("Status", typeof(TodoStatus), "Done");
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Single(result);
        Assert.Equal(TodoStatus.Done, result[0].Status);
    }

    [Fact]
    public void Equals_FromString_CaseInsensitive()
    {
        var expr = EnumExpressionHelper.Equals<TodoView>("Status", typeof(TodoStatus), "inprogress");
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Single(result);
    }

    [Fact]
    public void EqualsAsString_ComparesToString()
    {
        var expr = EnumExpressionHelper.EqualsAsString<TodoView>("Status", "InProgress");
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Single(result);
        Assert.Equal(TodoStatus.InProgress, result[0].Status);
    }

    [Fact]
    public void EqualsAsString_FromEnum_ComparesToString()
    {
        var expr = EnumExpressionHelper.EqualsAsString<TodoView, TodoStatus>("Status", TodoStatus.Done);
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Single(result);
        Assert.Equal(TodoStatus.Done, result[0].Status);
    }

    [Fact]
    public void EqualsAsInt_ComparesAsInteger()
    {
        var expr = EnumExpressionHelper.EqualsAsInt<TodoView, TodoStatus>("Status", TodoStatus.InProgress);
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Single(result);
        Assert.Equal(TodoStatus.InProgress, result[0].Status);
    }

    [Fact]
    public void EqualsAsInt_FromString_ComparesAsInteger()
    {
        var expr = EnumExpressionHelper.EqualsAsInt<TodoView>("Status", typeof(TodoStatus), "Done");
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Single(result);
        Assert.Equal(TodoStatus.Done, result[0].Status);
    }

    [Fact]
    public void ExpressionToString_ShowsDifference()
    {
        var asEnum = EnumExpressionHelper.Equals<TodoView, TodoStatus>("Status", TodoStatus.InProgress);
        var asString = EnumExpressionHelper.EqualsAsString<TodoView>("Status", "InProgress");
        var asInt = EnumExpressionHelper.EqualsAsInt<TodoView, TodoStatus>("Status", TodoStatus.InProgress);

        // All three produce different expression trees
        var enumStr = asEnum.Body.ToString();
        var stringStr = asString.Body.ToString();
        var intStr = asInt.Body.ToString();

        Assert.DoesNotContain("Convert", enumStr);     // No Convert — native enum
        Assert.Contains("ToString", stringStr);          // ToString() call
        Assert.Contains("Convert", intStr);              // Explicit Convert to Int32
    }
}

public class PropertyPathTests
{
    [Fact]
    public void Resolve_SimpleProperty()
    {
        var param = Expression.Parameter(typeof(TodoView), "x");
        var expr = PropertyPath.Resolve(param, "Title");
        Assert.Equal("x.Title", expr.ToString());
    }

    [Fact]
    public void Resolve_NestedProperty()
    {
        var param = Expression.Parameter(typeof(TodoView), "x");
        var expr = PropertyPath.Resolve(param, "Customer.Name");
        Assert.Equal("x.Customer.Name", expr.ToString());
    }

    [Fact]
    public void GetPropertyType_Simple()
    {
        var type = PropertyPath.GetPropertyType(typeof(TodoView), "Title");
        Assert.Equal(typeof(string), type);
    }

    [Fact]
    public void GetPropertyType_Nested()
    {
        var type = PropertyPath.GetPropertyType(typeof(TodoView), "Customer.Name");
        Assert.Equal(typeof(string), type);
    }

    [Fact]
    public void GetPropertyType_InvalidPath_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            PropertyPath.GetPropertyType(typeof(TodoView), "NonExistent.Foo"));
    }
}

public class NestedPropertyTests
{
    private static readonly Guid AcmeId = Guid.NewGuid();
    private static readonly Guid BetaId = Guid.NewGuid();

    private static readonly List<TodoView> TestData =
    [
        new() { Title = "A", Customer = new() { Id = AcmeId, Name = "Acme", IsActive = true } },
        new() { Title = "B", Customer = new() { Id = BetaId, Name = "Beta Corp", IsActive = false } },
        new() { Title = "C", Customer = new() { Id = AcmeId, Name = "Acme", IsActive = true } },
    ];

    [Fact]
    public void Equal_NestedProperty()
    {
        var expr = ExpressionHelper.Equal<TodoView, string>("Customer.Name", "Acme");
        var result = TestData.AsQueryable().Where(expr).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void StartsWith_NestedProperty()
    {
        var expr = ExpressionHelper.StartsWith<TodoView>("Customer.Name", "Beta");
        var result = TestData.AsQueryable().Where(expr).ToList();
        Assert.Single(result);
    }

    [Fact]
    public void OrderBy_NestedProperty()
    {
        var selector = ExpressionHelper.OrderBy<TodoView>("Customer.Name");
        Assert.Contains("Customer", selector.Body.ToString());
        Assert.Contains("Name", selector.Body.ToString());
    }

    [Fact]
    public void Contains_NestedProperty()
    {
        var ids = new List<Guid> { AcmeId };

        var expr = ExpressionHelper.Contains<TodoView, Guid>("Customer.Id", ids);
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Equal(2, result.Count); // Both Acme entries
    }
}

public class CollectionNavigationTests
{
    private static readonly Guid UserId1 = Guid.NewGuid();
    private static readonly Guid UserId2 = Guid.NewGuid();

    private static readonly List<TodoView> TestData =
    [
        new() { Title = "A", Responsibles = [new() { Id = UserId1, Name = "Alice" }] },
        new() { Title = "B", Responsibles = [new() { Id = UserId2, Name = "Bob" }] },
        new() { Title = "C", Responsibles = [new() { Id = UserId1, Name = "Alice" }, new() { Id = UserId2, Name = "Bob" }] },
        new() { Title = "D", Responsibles = [] },
    ];

    [Fact]
    public void Any_MatchesByProperty()
    {
        var expr = ExpressionHelper.Any<TodoView, ResponsibleView, Guid>("Responsibles", "Id", UserId1);
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.Title == "A");
        Assert.Contains(result, t => t.Title == "C");
    }

    [Fact]
    public void Any_WithCustomPredicate()
    {
        Expression<Func<ResponsibleView, bool>> predicate = r => r.Name == "Bob";
        var expr = ExpressionHelper.Any<TodoView, ResponsibleView>("Responsibles", predicate);
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void HasAny_ChecksNonEmpty()
    {
        var expr = ExpressionHelper.HasAny<TodoView, ResponsibleView>("Responsibles");
        var result = TestData.AsQueryable().Where(expr).ToList();

        Assert.Equal(3, result.Count); // D has empty list
    }
}

public class FilterBuilderTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Customer1 = Guid.NewGuid();
    private static readonly Guid Customer2 = Guid.NewGuid();

    private static readonly List<TodoView> TestData =
    [
        new() { Title = "Active Todo", Status = TodoStatus.New, IsArchived = false, CustomerId = Customer1,
                Responsibles = [new() { Id = UserId }], DueDate = new(2025, 7, 1) },
        new() { Title = "Done Todo", Status = TodoStatus.Done, IsArchived = false, CustomerId = Customer2,
                Responsibles = [], DueDate = new(2025, 6, 1) },
        new() { Title = "Archived Todo", Status = TodoStatus.Archived, IsArchived = true, CustomerId = Customer1,
                Responsibles = [new() { Id = UserId }], DueDate = new(2025, 1, 1) },
    ];

    [Fact]
    public void Build_NoFilters_ReturnsAll()
    {
        var filter = new FilterBuilder<TodoView>().Build();
        var result = TestData.AsQueryable().Where(filter).ToList();
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Build_SingleFilter()
    {
        var filter = new FilterBuilder<TodoView>()
            .Where("IsArchived", false)
            .Build();

        var result = TestData.AsQueryable().Where(filter).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Build_MultipleFilters_CombinesWithAnd()
    {
        var filter = new FilterBuilder<TodoView>()
            .Where("IsArchived", false)
            .WhereEnum("Status", TodoStatus.New)
            .Build();

        var result = TestData.AsQueryable().Where(filter).ToList();
        Assert.Single(result);
        Assert.Equal("Active Todo", result[0].Title);
    }

    [Fact]
    public void WhereIf_True_AddsFilter()
    {
        var customerIds = new List<Guid> { Customer1 };

        var filter = new FilterBuilder<TodoView>()
            .WhereIf(customerIds.Count > 0, b => b.Contains("CustomerId", customerIds))
            .Build();

        var result = TestData.AsQueryable().Where(filter).ToList();
        Assert.Equal(2, result.Count); // Customer1 has 2 todos
    }

    [Fact]
    public void WhereIf_False_SkipsFilter()
    {
        var customerIds = new List<Guid>();

        var filter = new FilterBuilder<TodoView>()
            .WhereIf(customerIds.Count > 0, b => b.Contains("CustomerId", customerIds))
            .Build();

        var result = TestData.AsQueryable().Where(filter).ToList();
        Assert.Equal(3, result.Count); // No filter applied
    }

    [Fact]
    public void WhereAny_FiltersCollection()
    {
        var filter = new FilterBuilder<TodoView>()
            .WhereAny<ResponsibleView, Guid>("Responsibles", "Id", UserId)
            .Build();

        var result = TestData.AsQueryable().Where(filter).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void WhereStartsWith_Works()
    {
        var filter = new FilterBuilder<TodoView>()
            .WhereStartsWith("Title", "Active")
            .Build();

        var result = TestData.AsQueryable().Where(filter).ToList();
        Assert.Single(result);
    }

    [Fact]
    public void WhereGreaterThan_Works()
    {
        var filter = new FilterBuilder<TodoView>()
            .WhereGreaterThan("DueDate", new DateTime(2025, 6, 15))
            .Build();

        var result = TestData.AsQueryable().Where(filter).ToList();
        Assert.Single(result);
        Assert.Equal("Active Todo", result[0].Title);
    }

    [Fact]
    public void BuildOr_CombinesWithOr()
    {
        var filter = new FilterBuilder<TodoView>()
            .WhereEnum("Status", TodoStatus.New)
            .WhereEnum("Status", TodoStatus.Done)
            .BuildOr();

        var result = TestData.AsQueryable().Where(filter).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void WhereEnumAsString_Works()
    {
        var filter = new FilterBuilder<TodoView>()
            .WhereEnumAsString("Status", "New")
            .Build();

        var result = TestData.AsQueryable().Where(filter).ToList();
        Assert.Single(result);
    }

    [Fact]
    public void ComplexScenario_AbacStyle()
    {
        // Simulates: "User sees own todos from managed customers, excluding archived"
        var managedCustomerIds = new List<Guid> { Customer1 };
        var hasCustomers = managedCustomerIds.Count > 0;

        var filter = new FilterBuilder<TodoView>()
            .Where("IsArchived", false)
            .WhereIf(hasCustomers, b => b.Contains("CustomerId", managedCustomerIds))
            .WhereAny<ResponsibleView, Guid>("Responsibles", "Id", UserId)
            .Build();

        var result = TestData.AsQueryable().Where(filter).ToList();
        Assert.Single(result);
        Assert.Equal("Active Todo", result[0].Title);
    }

    [Fact]
    public void Count_TracksFilters()
    {
        var builder = new FilterBuilder<TodoView>();
        Assert.True(builder.IsEmpty);
        Assert.Equal(0, builder.Count);

        builder.Where("IsArchived", false);
        Assert.False(builder.IsEmpty);
        Assert.Equal(1, builder.Count);
    }

    [Fact]
    public void Clear_RemovesAllFilters()
    {
        var builder = new FilterBuilder<TodoView>();
        builder.Where("IsArchived", false);
        builder.Where("Title", "test");
        Assert.Equal(2, builder.Count);

        builder.Clear();
        Assert.True(builder.IsEmpty);
    }
}

public class ExpressionRewriterTests
{
    [Fact]
    public void RewriteEnumConversions_StripsConvertInt32()
    {
        // Simulate what C# compiler generates: Convert(x.Status, Int32) == Convert(enum, Int32)
        var param = Expression.Parameter(typeof(TodoView), "x");
        var property = Expression.Property(param, "Status");
        var convertLeft = Expression.Convert(property, typeof(int));
        var convertRight = Expression.Convert(Expression.Constant(TodoStatus.InProgress, typeof(TodoStatus)), typeof(int));
        var equality = Expression.Equal(convertLeft, convertRight);
        var lambda = Expression.Lambda<Func<TodoView, bool>>(equality, param);

        // Before rewrite: has Convert nodes
        Assert.Contains("Convert", lambda.Body.ToString());

        // Rewrite
        var rewritten = ExpressionRewriter.RewriteEnumConversions(lambda);

        // After rewrite: no Convert nodes
        Assert.DoesNotContain("Convert", rewritten.Body.ToString());

        // Still works correctly
        var data = new List<TodoView>
        {
            new() { Status = TodoStatus.New },
            new() { Status = TodoStatus.InProgress },
            new() { Status = TodoStatus.Done },
        };

        var result = data.AsQueryable().Where(rewritten).ToList();
        Assert.Single(result);
        Assert.Equal(TodoStatus.InProgress, result[0].Status);
    }

    [Fact]
    public void RewriteEnumConversions_LeavesNonEnumExpressionsAlone()
    {
        Expression<Func<TodoView, bool>> original = x => x.Title == "test";
        var rewritten = ExpressionRewriter.RewriteEnumConversions(original);

        // Should be unchanged
        Assert.Equal(original.Body.ToString(), rewritten.Body.ToString());
    }
}
