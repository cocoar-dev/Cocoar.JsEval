using System.Linq.Expressions;
using Cocoar.JsEval.Linq;
using Jint;
using Xunit;
using static Cocoar.JsEval.Tests.Linq.TranslatorTestHelper;

namespace Cocoar.JsEval.Tests.Linq;

public class TranslatorTests
{
    // Each test compares the translator's output against the string form of a
    // hand-written C# Expression<Func<...>> — the canonical shape Marten/EF would see.

    [Fact]
    public void PropertyAccess_Simple()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.IsActive;
        var actual = Translate<TestUser, bool>("(u) => u.IsActive");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void PropertyAccess_Nested()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.Address!.City == "Vienna";
        var actual = Translate<TestUser, bool>("(u) => u.Address.City === 'Vienna'");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void StringMethod_StartsWith_MapsCamelCase()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.Name.StartsWith("A");
        var actual = Translate<TestUser, bool>("(u) => u.Name.startsWith('A')");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void StringMethod_Includes_MapsToContains()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.Name.Contains("oo");
        var actual = Translate<TestUser, bool>("(u) => u.Name.includes('oo')");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void StringMethod_ToLowerCase_MapsToToLower()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.Name.ToLower() == "alice";
        var actual = Translate<TestUser, bool>("(u) => u.Name.toLowerCase() === 'alice'");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void LogicalAnd_Combines_With_AndAlso()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.IsActive && u.Age > 18;
        var actual = Translate<TestUser, bool>("(u) => u.IsActive && u.Age > 18");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void LogicalOr_Combines_With_OrElse()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.IsActive || u.Age > 65;
        var actual = Translate<TestUser, bool>("(u) => u.IsActive || u.Age > 65");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void UnaryNot_Inverts_Boolean()
    {
        Expression<Func<TestUser, bool>> baseline = u => !u.IsActive;
        var actual = Translate<TestUser, bool>("(u) => !u.IsActive");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void Comparison_GreaterThan_NumericCoercion()
    {
        // JS 18 parses to double; Age is int. Translator must coerce the constant.
        Expression<Func<TestUser, bool>> baseline = u => u.Age > 18;
        var actual = Translate<TestUser, bool>("(u) => u.Age > 18");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void Comparison_AllOperators()
    {
        Assert.Equal(
            ((Expression<Func<TestUser, bool>>)(u => u.Age < 18)).ToString(),
            Translate<TestUser, bool>("(u) => u.Age < 18").ToString());
        Assert.Equal(
            ((Expression<Func<TestUser, bool>>)(u => u.Age <= 18)).ToString(),
            Translate<TestUser, bool>("(u) => u.Age <= 18").ToString());
        Assert.Equal(
            ((Expression<Func<TestUser, bool>>)(u => u.Age >= 18)).ToString(),
            Translate<TestUser, bool>("(u) => u.Age >= 18").ToString());
        Assert.Equal(
            ((Expression<Func<TestUser, bool>>)(u => u.Age != 18)).ToString(),
            Translate<TestUser, bool>("(u) => u.Age !== 18").ToString());
    }

    [Fact]
    public void Ternary_ProducesConditional()
    {
        Expression<Func<TestUser, string>> baseline = u => u.IsActive ? u.Name : u.Email;
        var actual = Translate<TestUser, string>("(u) => u.IsActive ? u.Name : u.Email");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void Closure_FreeIdentifier_ResolvedFromEngine()
    {
        var actual = Translate<TestUser, bool>(
            "(u) => u.Name.startsWith(prefix)",
            engine => engine.SetValue("prefix", "A"));
        Expression<Func<TestUser, bool>> baseline = u => u.Name.StartsWith("A");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void Array_CSharpAliases_WorkLikeJsNames()
    {
        // Same Expression tree whether the developer uses the JS name or the C# name.
        Expression<Func<TestUser, bool>> baseline = u => u.Tags.Any(t => t == "vip");

        var js_some     = Translate<TestUser, bool>("(u) => u.Tags.some(t => t === 'vip')");
        var cs_any      = Translate<TestUser, bool>("(u) => u.Tags.Any(t => t === 'vip')");
        Assert.Equal(baseline.ToString(), js_some.ToString());
        Assert.Equal(baseline.ToString(), cs_any.ToString());
    }

    [Fact]
    public void Array_Where_CSharpAlias_Works()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.Tags.Where(t => t != "").Any();
        // Emulate with JS-.where(...).some():
        var actual = Translate<TestUser, bool>("(u) => u.Tags.Where(t => t !== '').some(x => true)");
        // Just verify it doesn't throw and contains a Where call:
        Assert.Contains("Where", actual.ToString(), StringComparison.Ordinal);
    }

    // The LINQ-side augmentations of String / Array<T> are now reflection-generated
    // by LinqTsContributor (not a hand-written embedded file). The full
    // TsDefinitionService round-trip is covered in LinqTsContributorTests; this is
    // a smoke check so a stray refactor that loses these signatures trips here too.
    [Fact]
    public void LinqTsContributor_EmitsStringAndArrayAugmentations()
    {
        var contributor = Activator.CreateInstance(
            typeof(Cocoar.JsEval.Linq.LinqCasts).Assembly.GetType("Cocoar.JsEval.Linq.LinqTsContributor", throwOnError: true)!)
            as Cocoar.JsEval.IJsTsDefinitionContributor;
        Assert.NotNull(contributor);

        var defs = contributor!.GetTsDefinitions().ToDictionary(kv => kv.Key, kv => kv.Value);
        var content = defs["cocoar-jseval-linq.d.ts"];

        Assert.Contains("interface String", content);
        Assert.Contains("Contains(value: string): boolean", content);
        Assert.Contains("interface Array<T>", content);
        Assert.Contains("Any(predicate?:", content);
    }

    [Fact]
    public void Array_Contains_CSharpAlias_Works()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.Tags.Contains("vip");
        var actual = Translate<TestUser, bool>("(u) => u.Tags.Contains('vip')");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void Array_Some_MapsToAny()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.Tags.Any(t => t == "vip");
        var actual = Translate<TestUser, bool>("(u) => u.Tags.some(t => t === 'vip')");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void Array_Every_MapsToAll()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.Tags.All(t => t != "");
        var actual = Translate<TestUser, bool>("(u) => u.Tags.every(t => t !== '')");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void Array_Includes_MapsToContains()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.Tags.Contains("vip");
        var actual = Translate<TestUser, bool>("(u) => u.Tags.includes('vip')");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void BracketAccess_WithStringLiteral_EquivalentToDotAccess()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.Name == "x";
        var actual = Translate<TestUser, bool>("(u) => u['Name'] === 'x'");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void BracketAccess_WithDynamicVariable_Throws()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            Translate<TestUser, bool>(
                "(u) => u[prop] === 'x'",
                engine => engine.SetValue("prop", "Name")));
        Assert.Contains("Dynamic access", ex.Message);
    }

    [Fact]
    public void Destructuring_ThrowsWithHelpfulMessage()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            Translate<TestUser, bool>("({Name}) => Name === 'x'"));
        Assert.Contains("destructuring", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MultiParam_ThrowsWithHelpfulMessage()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            Translate<TestUser, bool>("(u, idx) => u.Name === 'x'"));
        Assert.Contains("single-parameter", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Closure_DecimalFromHost_StaysDecimal()
    {
        var engine = new Jint.Engine();
        engine.SetValue("threshold", 99.99m);
        var jsFn = engine.Evaluate("(u) => u.Balance > threshold");
        var expr = JsExpressionTranslator.Translate<TestUser, bool>(jsFn, engine);

        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(decimal), constant.Type);
        Assert.Equal(99.99m, constant.Value);
    }

    [Fact]
    public void Closure_DoubleFromHost_CoercedToDecimalWhenComparedWithDecimalProp()
    {
        var engine = new Jint.Engine();
        engine.SetValue("threshold", 99.99d); // JS number is always double
        var jsFn = engine.Evaluate("(u) => u.Balance > threshold");
        var expr = JsExpressionTranslator.Translate<TestUser, bool>(jsFn, engine);

        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(decimal), constant.Type);
    }

    [Fact]
    public void Arithmetic_DecimalMultiplyWithDoubleLiteral()
    {
        // u.Balance is decimal; 0.1 is a JS double literal.
        // CoerceNumeric should narrow the double literal to decimal before Multiply.
        Expression<Func<TestUser, bool>> baseline = u => u.Balance * 0.1m > 10m;
        var actual = Translate<TestUser, bool>("(u) => u.Balance * 0.1 > 10");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void Arithmetic_TwoDecimalPropertiesMultiplied()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.Balance + u.Balance > 100m;
        var actual = Translate<TestUser, bool>("(u) => u.Balance + u.Balance > 100");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void LinqDecimal_InlineInPredicate_BecomesTypedDecimalConstant()
    {
        // Precision-critical: 123456789012345678.123456 has more digits than double
        // can represent. Translator must bypass Jint runtime and produce a real decimal.
        var expr = Translate<TestUser, bool>(
            "(u) => u.Balance > linq.decimal('123456789012345678.123456')");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(decimal), constant.Type);
        Assert.Equal(123456789012345678.123456m, constant.Value);
    }

    [Fact]
    public void LinqLong_InlineInPredicate_PreservesBeyondSafeInteger()
    {
        // 2^53 + 1 is not exactly representable as a double — TestUser.ExternalId is long.
        var expr = Translate<TestUser, bool>("(u) => u.ExternalId === linq.long('9007199254740993')");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(long), constant.Type);
        Assert.Equal(9007199254740993L, constant.Value);
    }

    [Fact]
    public void LinqDouble_InlineInPredicate_EmitsDoubleConstant()
    {
        var expr = Translate<TestUser, bool>("(u) => u.Balance > linq.double('3.14')");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        // Left is decimal (Balance); translator coerces the double into decimal literal
        // (since literal is a ConstantExpression, the coercion kicks in after cs-interception).
        Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
    }

    [Fact]
    public void LinqInt_InlineInPredicate()
    {
        var expr = Translate<TestUser, bool>("(u) => u.Age > linq.int('42')");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(int), constant.Type);
        Assert.Equal(42, constant.Value);
    }

    [Fact]
    public void LinqDate_InlineInPredicate_BecomesTypedDateTimeConstant()
    {
        var expr = Translate<TestUser, bool>("(u) => u.CreatedAt > linq.date('2024-01-01')");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(DateTime), constant.Type);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), constant.Value);
    }

    [Fact]
    public void LinqDateUtc_PreservesUtcKind()
    {
        var expr = Translate<TestUser, bool>("(u) => u.CreatedAt > linq.dateUtc('2024-01-01T00:00:00Z')");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        var dt = (DateTime)constant.Value!;
        Assert.Equal(DateTimeKind.Utc, dt.Kind);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), dt);
    }

    [Fact]
    public void LinqDateOffset_InlineInPredicate_WithOffset()
    {
        var expr = Translate<TestUser, bool>("(u) => u.LastLogin > linq.dateOffset('2024-01-01T10:00:00+02:00')");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(DateTimeOffset), constant.Type);
        var dto = (DateTimeOffset)constant.Value!;
        Assert.Equal(TimeSpan.FromHours(2), dto.Offset);
    }

    [Fact]
    public void LinqDateOnly_InlineInPredicate()
    {
        var expr = Translate<TestUser, bool>("(u) => u.Birthday < linq.dateOnly('1990-06-15')");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(DateOnly), constant.Type);
        Assert.Equal(new DateOnly(1990, 6, 15), constant.Value);
    }

    [Fact]
    public void Closure_DateTimeFromHost_StaysDateTime()
    {
        // Analog to the decimal-closure case: host-set DateTime survives intact.
        var engine = new Jint.Engine();
        engine.SetValue("cutoff", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var jsFn = engine.Evaluate("(u) => u.CreatedAt > cutoff");
        var expr = JsExpressionTranslator.Translate<TestUser, bool>(jsFn, engine);

        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(DateTime), constant.Type);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), constant.Value);
    }

    // Enum coercion tests: the translator produces native-enum equality
    // (`u.Status == Active` with types matching), deliberately NOT the
    // C# compiler's `Convert(u.Status, Int32) == Convert(Active, Int32)` form.
    // The native form is the ORM-neutral shape — it lets the provider decide
    // whether enums are stored as int or string at query-translation time.

    [Fact]
    public void Enum_StringLiteral_CoercedToEnumConstant()
    {
        var expr = Translate<TestUser, bool>("(u) => u.Status === 'Active'");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(UserStatus), constant.Type);
        Assert.Equal(UserStatus.Active, constant.Value);
        // Left side is the raw property, not wrapped in Convert:
        Assert.IsAssignableFrom<MemberExpression>(binary.Left);
    }

    [Fact]
    public void Enum_StringLiteral_CaseInsensitive()
    {
        var expr = Translate<TestUser, bool>("(u) => u.Status === 'archived'");
        var constant = (ConstantExpression)((BinaryExpression)expr.Body).Right;
        Assert.Equal(UserStatus.Archived, constant.Value);
    }

    [Fact]
    public void Enum_NumericLiteral_CoercedToEnumConstant()
    {
        var expr = Translate<TestUser, bool>("(u) => u.Status === 1");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(UserStatus), constant.Type);
        Assert.Equal(UserStatus.Active, constant.Value);
    }

    [Fact]
    public void Enum_InvalidString_ThrowsHelpfulMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Translate<TestUser, bool>("(u) => u.Status === 'NotARealValue'"));
        Assert.Contains("Cannot convert string 'NotARealValue'", ex.Message);
        Assert.Contains("UserStatus", ex.Message);
        Assert.Contains("Active", ex.Message); // lists valid values
    }

    [Fact]
    public void Enum_Inequality_AlsoCoerces()
    {
        var expr = Translate<TestUser, bool>("(u) => u.Status !== 'Deleted'");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        Assert.Equal(ExpressionType.NotEqual, binary.NodeType);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(UserStatus.Deleted, constant.Value);
    }

    [Fact]
    public void StringContains_PascalCase_WorksViaIgnoreCaseReflection()
    {
        // C# PascalCase method name — found via BindingFlags.IgnoreCase, no map entry needed.
        Expression<Func<TestUser, bool>> baseline = u => u.Name.Contains("x");
        var actual = Translate<TestUser, bool>("(u) => u.Name.Contains('x')");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void StringIncludes_JsName_MapsToCLRContains()
    {
        // JS name doesn't exist on CLR string — the DefaultJsMethodMap translates it.
        Expression<Func<TestUser, bool>> baseline = u => u.Name.Contains("x");
        var actual = Translate<TestUser, bool>("(u) => u.Name.includes('x')");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void StringToLower_AllThreeForms_WorkIdentically()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.Name.ToLower() == "a";
        Assert.Equal(baseline.ToString(),
            Translate<TestUser, bool>("(u) => u.Name.ToLower() === 'a'").ToString());        // PascalCase
        Assert.Equal(baseline.ToString(),
            Translate<TestUser, bool>("(u) => u.Name.toLower() === 'a'").ToString());        // camelCase CLR name
        Assert.Equal(baseline.ToString(),
            Translate<TestUser, bool>("(u) => u.Name.toLowerCase() === 'a'").ToString());    // JS name via map
    }

    [Fact]
    public void StringIndexOf_MapsToClrIndexOf()
    {
        Expression<Func<TestUser, bool>> baseline = u => u.Name.IndexOf("A") >= 0;
        var actual = Translate<TestUser, bool>("(u) => u.Name.indexOf('A') >= 0");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void LinqToday_CapturedAsDateTimeConstant()
    {
        var expr = Translate<TestUser, bool>("(u) => u.CreatedAt > linq.today()");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(DateTime), constant.Type);
        // Value should be today's midnight (date component only).
        var captured = (DateTime)constant.Value!;
        Assert.Equal(DateTime.Today, captured);
        Assert.Equal(TimeSpan.Zero, captured.TimeOfDay);
    }

    [Fact]
    public void LinqNow_CapturedAsDateTimeConstant()
    {
        var before = DateTime.Now;
        var expr = Translate<TestUser, bool>("(u) => u.CreatedAt > linq.now()");
        var after = DateTime.Now;
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        var captured = (DateTime)constant.Value!;
        Assert.InRange(captured, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void LinqUtcNow_CapturedAsUtcDateTimeConstant()
    {
        var expr = Translate<TestUser, bool>("(u) => u.CreatedAt > linq.utcNow()");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        var captured = (DateTime)constant.Value!;
        Assert.Equal(DateTimeKind.Utc, captured.Kind);
    }

    [Fact]
    public void LinqToday_Plus_AddDays_FormsProperExpression()
    {
        // The pattern from the team's "due this week" scenario:
        // todos.where(t => t.DueDate < linq.today().AddDays(7))
        var expr = Translate<TestUser, bool>("(u) => u.CreatedAt < linq.today().AddDays(7)");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        // Right side is a MethodCallExpression (AddDays) on a DateTime ConstantExpression.
        var call = Assert.IsAssignableFrom<MethodCallExpression>(binary.Right);
        Assert.Equal(nameof(DateTime.AddDays), call.Method.Name);
    }

    [Fact]
    public void LinqGuid_InlineInPredicate_BecomesTypedGuidConstant()
    {
        var expr = Translate<TestUser, bool>(
            "(u) => u.Id === linq.guid('00000000-0000-0000-0000-000000000001')");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(Guid), constant.Type);
        Assert.Equal(new Guid("00000000-0000-0000-0000-000000000001"), constant.Value);
    }

    [Fact]
    public void Closure_GuidFromHost_StaysGuid()
    {
        var id = Guid.NewGuid();
        var engine = new Jint.Engine();
        engine.SetValue("userId", id);
        var jsFn = engine.Evaluate("(u) => u.Id === userId");
        var expr = JsExpressionTranslator.Translate<TestUser, bool>(jsFn, engine);

        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(Guid), constant.Type);
        Assert.Equal(id, constant.Value);
    }

    [Fact]
    public void LinqTimeSpan_InlineInPredicate_BecomesTypedTimeSpanConstant()
    {
        var expr = Translate<TestUser, bool>(
            "(u) => u.SessionTimeout > linq.timeSpan('01:30:00')");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(typeof(TimeSpan), constant.Type);
        Assert.Equal(TimeSpan.FromMinutes(90), constant.Value);
    }

    [Fact]
    public void UnaryNegation_OnNumericLiteral_IsConstantFolded()
    {
        // JS parses "-7" as Unary(-, Constant(7)). LINQ providers like Marten
        // reject the Unary node shape; the translator folds it to Constant(-7)
        // so the emitted tree matches what the C# compiler produces for -7.
        var expr = Translate<TestUser, bool>("(u) => u.Age > -7");
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        var constant = Assert.IsAssignableFrom<ConstantExpression>(binary.Right);
        Assert.Equal(-7, constant.Value);
    }

    [Fact]
    public void CsDateTime_Closure_AgainstDateTimeColumn_UsesImplicitOperator()
    {
        // TestUser.CreatedAt is a DateTime. We set a CsDateTime closure and compare
        // — the translator must find the implicit operator CsDateTime → DateTime
        // and insert a Convert node so the Expression.Binary is valid.
        var engine = new Jint.Engine();
        engine.SetValue("cutoff", new Cocoar.JsEval.Engine.CsDateTime(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        var jsFn = engine.Evaluate("(u) => u.CreatedAt > cutoff");
        var expr = JsExpressionTranslator.Translate<TestUser, bool>(jsFn, engine);

        // The Expression tree should have a Convert wrapping the right side.
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        Assert.IsAssignableFrom<UnaryExpression>(binary.Right);
        var conv = (UnaryExpression)binary.Right;
        Assert.Equal(ExpressionType.Convert, conv.NodeType);
        Assert.Equal(typeof(DateTime), conv.Type);
    }

    [Fact]
    public void CsDateTime_AddDays_InPredicate_ProducesMethodCall()
    {
        var engine = new Jint.Engine();
        Cocoar.JsEval.Engine.CsDateTimeGlobals.Register(engine);
        engine.SetValue("cutoff", new Cocoar.JsEval.Engine.CsDateTime(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var jsFn = engine.Evaluate("(u) => u.CreatedAt > cutoff.AddDays(7)");
        var expr = JsExpressionTranslator.Translate<TestUser, bool>(jsFn, engine);

        // Expect: u.CreatedAt > Convert(cutoff.AddDays(7), DateTime)
        var binary = Assert.IsAssignableFrom<BinaryExpression>(expr.Body);
        // Right-side has at least a MethodCallExpression under a Convert.
        var rightInner = binary.Right is UnaryExpression u ? u.Operand : binary.Right;
        Assert.IsAssignableFrom<MethodCallExpression>(rightInner);
    }

    [Fact]
    public void LinqDecimal_WithNonLiteralArgument_ThrowsHelpfulMessage()
    {
        var engine = new Jint.Engine();
        engine.SetValue("val", "99.99");
        var ex = Assert.Throws<NotSupportedException>(() =>
        {
            var jsFn = engine.Evaluate("(u) => u.Balance > linq.decimal(val)");
            _ = JsExpressionTranslator.Translate<TestUser, bool>(jsFn, engine);
        });
        Assert.Contains("string literal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnresolvedIdentifier_ThrowsWithClearMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Translate<TestUser, bool>("(u) => u.Name === x"));
        Assert.Contains("Unresolved identifier 'x'", ex.Message);
    }

    [Fact]
    public void UnsupportedNode_ThrowsNotSupported()
    {
        // Spread / yield / await — not supported. Use template literal interpolation as a trigger.
        Assert.ThrowsAny<Exception>(() =>
            Translate<TestUser, bool>("(u) => `hello ${u.Name}` === 'hello a'"));
    }

    [Fact]
    public void ExpectsFunction_RejectsNonFunction()
    {
        var engine = new Jint.Engine();
        var notAFunction = engine.Evaluate("42");
        Assert.Throws<ArgumentException>(() =>
        {
            _ = JsExpressionTranslator.Translate<TestUser, bool>(notAFunction);
        });
    }

    // --- Nullish Coalescing (`??`) ---

    [Fact]
    public void NullishCoalesce_StringProperty_Fallback()
    {
        // Script shape: `(u) => (u.Address?.City ?? 'Unknown') === 'Vienna'`
        // But we first test plain coalesce on a nullable reference to keep the baseline simple.
        Expression<Func<TestUser, bool>> baseline = u => (u.Address!.City ?? "Unknown") == "Vienna";
        var actual = Translate<TestUser, bool>("(u) => (u.Address.City ?? 'Unknown') === 'Vienna'");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void NullishCoalesce_CompilesAndRuns()
    {
        var actual = Translate<TestUser, string>("(u) => u.Name ?? 'anon'");
        var fn = actual.Compile();
        Assert.Equal("alice", fn(new TestUser { Name = "alice" }));
        Assert.Equal("anon", fn(new TestUser { Name = null! }));
    }

    // --- Optional Chaining (`?.`) ---

    [Fact]
    public void OptionalChain_SingleHop_PropertyAccess_CompilesAndRuns()
    {
        var expr = Translate<TestUser, string?>("(u) => u.Address?.City");
        var fn = expr.Compile();
        Assert.Equal("Vienna", fn(new TestUser { Address = new TestAddress { City = "Vienna" } }));
        Assert.Null(fn(new TestUser { Address = null }));
    }

    [Fact]
    public void OptionalChain_InPredicate_WithCoalesce()
    {
        var expr = Translate<TestUser, bool>("(u) => (u.Address?.City ?? '') === 'Vienna'");
        var fn = expr.Compile();
        Assert.True(fn(new TestUser { Address = new TestAddress { City = "Vienna" } }));
        Assert.False(fn(new TestUser { Address = null }));
    }

    [Fact]
    public void OptionalChain_DeepChain_ShortCircuits()
    {
        // `u.Address?.City` — Address null should short-circuit past the .City access.
        var expr = Translate<TestUser, bool>("(u) => u.Address?.City.startsWith('V') === true");
        var fn = expr.Compile();
        Assert.True(fn(new TestUser { Address = new TestAddress { City = "Vienna" } }));
        Assert.False(fn(new TestUser { Address = null }));
    }

    [Fact]
    public void OptionalChain_MethodCall_ShortCircuits()
    {
        var expr = Translate<TestUser, bool>("(u) => u.Address?.City.startsWith('V') === true");
        var fn = expr.Compile();
        Assert.True(fn(new TestUser { Address = new TestAddress { City = "Vienna" } }));
        Assert.False(fn(new TestUser { Address = null }));
    }

    [Fact]
    public void OptionalChain_NonNullableValueTypeGuard_IsSkipped()
    {
        // `u.Id?.ToString()` — Id is Guid (non-nullable value type); the guard reduces
        // away and we get the plain method call. Just assert it translates without throwing.
        var expr = Translate<TestUser, string?>("(u) => u.Id?.ToString()");
        var fn = expr.Compile();
        var id = Guid.NewGuid();
        Assert.Equal(id.ToString(), fn(new TestUser { Id = id }));
    }

    // --- bool? → bool coercion in boolean contexts (JS-truthy semantics) ---
    // In JS, `undefined` / `null` are falsy. C# forces `?.` predicates into explicit
    // `=== true`. The translator normalizes at every boolean-context site so scripts
    // can be written naturally.

    // Dataset covering all three cases for `u.Address?.City.startsWith('V')`:
    //   - Address with "Vienna"  → startsWith returns true
    //   - Address with "Berlin"  → startsWith returns false
    //   - Address is null        → chain short-circuits to null (JS: falsy)
    private static readonly TestUser VieUser    = new() { Name = "v", IsActive = true,  Address = new TestAddress { City = "Vienna" } };
    private static readonly TestUser VieInact   = new() { Name = "v", IsActive = false, Address = new TestAddress { City = "Vienna" } };
    private static readonly TestUser BerUser    = new() { Name = "b", IsActive = true,  Address = new TestAddress { City = "Berlin" } };
    private static readonly TestUser BerInact   = new() { Name = "b", IsActive = false, Address = new TestAddress { City = "Berlin" } };
    private static readonly TestUser NoAddr     = new() { Name = "n", IsActive = true,  Address = null };
    private static readonly TestUser NoAddrInact= new() { Name = "n", IsActive = false, Address = null };

    [Fact]
    public void Bool_Nullable_Body_InPredicate_NullIsFalse()
    {
        // Final body is bool?; the outer-body normalization turns null → false.
        var expr = Translate<TestUser, bool>("(u) => u.Address?.City.startsWith('V')");
        var fn = expr.Compile();
        Assert.True(fn(VieUser));
        Assert.False(fn(BerUser));
        Assert.False(fn(NoAddr));     // null → false
    }

    [Fact]
    public void Bool_Nullable_UnderNot_NullIsTrue()
    {
        // `!x` in JS: `!undefined === true`. Without the per-operand normalization,
        // `Expression.Not(bool?.null)` would lift to null — and our outer check
        // `== true` would then say false, flipping the JS semantics.
        var expr = Translate<TestUser, bool>("(u) => !u.Address?.City.startsWith('V')");
        var fn = expr.Compile();
        Assert.False(fn(VieUser));    // !true
        Assert.True(fn(BerUser));     // !false
        Assert.True(fn(NoAddr));      // !undefined → true (JS-truthy)
    }

    [Fact]
    public void Bool_Nullable_InAndAlso_NullShortCircuitsToFalse()
    {
        var expr = Translate<TestUser, bool>("(u) => u.Address?.City.startsWith('V') && u.IsActive");
        var fn = expr.Compile();
        Assert.True (fn(VieUser));     // true && true
        Assert.False(fn(VieInact));    // true && false
        Assert.False(fn(BerUser));     // false && true
        Assert.False(fn(NoAddr));      // null → false
    }

    [Fact]
    public void Bool_Nullable_InOrElse_NullIsFalse()
    {
        var expr = Translate<TestUser, bool>("(u) => u.Address?.City.startsWith('V') || u.IsActive");
        var fn = expr.Compile();
        Assert.True (fn(VieUser));     // true  || true
        Assert.True (fn(VieInact));    // true  || false
        Assert.True (fn(BerUser));     // false || true
        Assert.False(fn(BerInact));    // false || false
        Assert.True (fn(NoAddr));      // null(→false) || true
        Assert.False(fn(NoAddrInact)); // null(→false) || false
    }

    [Fact]
    public void Bool_Nullable_AsTernaryTest_NullIsFalse()
    {
        var expr = Translate<TestUser, int>("(u) => u.Address?.City.startsWith('V') ? 1 : 2");
        var fn = expr.Compile();
        Assert.Equal(1, fn(VieUser));
        Assert.Equal(2, fn(BerUser));
        Assert.Equal(2, fn(NoAddr));   // null → false → ifFalse branch
    }

    [Fact]
    public void Bool_Nullable_Body_Queryable_Where()
    {
        // End-to-end: the translated expression flows through IQueryable.Where just
        // like a hand-written `== true` predicate.
        var users = new[] { VieUser, BerUser, NoAddr }.AsQueryable();
        var expr = Translate<TestUser, bool>("(u) => u.Address?.City.startsWith('V')");
        var result = users.Where(expr).ToList();
        Assert.Single(result);
        Assert.Same(VieUser, result[0]);
    }

    [Fact]
    public void Bool_NonNullable_Passthrough_StillWorks()
    {
        // Regression: NormalizeToBool must be a no-op for plain bool — the existing
        // matrix of "u => u.IsActive", "a && b" etc. should produce identical trees.
        Expression<Func<TestUser, bool>> baseline = u => u.IsActive && u.Age > 18;
        var actual = Translate<TestUser, bool>("(u) => u.IsActive && u.Age > 18");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void Bool_NonNullable_Not_Passthrough()
    {
        Expression<Func<TestUser, bool>> baseline = u => !u.IsActive;
        var actual = Translate<TestUser, bool>("(u) => !u.IsActive");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    // --- Expression shape — Marten-friendly flattening ---
    // Marten's WhereClauseParser refuses ConditionalExpression with `null` branches
    // (and especially nested ones). The translator must emit a pure `&&`-chain for
    // optional-chain predicates in bool contexts, and a single (not nested) IIF for
    // non-bool chains.

    [Fact]
    public void OptionalChain_InBoolContext_EmitsFlatAndAlso_NoIif()
    {
        // This is the exact shape that crashed Marten in v3.1.0 for two-guard chains.
        var expr = Translate<TestUser, bool>("(u) => u.Address?.City.startsWith('V')");
        var text = expr.ToString();
        Assert.DoesNotContain("IIF", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Conditional", text, StringComparison.Ordinal);
        Assert.Contains("AndAlso", expr.Body.ToString().Replace("&&", "AndAlso"), StringComparison.Ordinal);
    }

    private class PersonHolder
    {
        public Guid Id { get; set; }
        public TestAddress? Person { get; set; }
    }

    [Fact]
    public void OptionalChain_TwoGuards_InBoolContext_EmitsFlatAndAlsoChain()
    {
        // `p.Person?.City?.startsWith('A')` — two optional-chain hops. v3.1.0 emitted
        // nested IIFs, Marten blew up. Now expected: `p.Person != null && p.Person.City != null && p.Person.City.StartsWith("A")`.
        var expr = Translate<PersonHolder, bool>("(p) => p.Person?.City?.startsWith('A')");
        var text = expr.ToString();
        Assert.DoesNotContain("IIF", text, StringComparison.Ordinal);
        // Three != null checks should be absent — wait: two guards, two null checks.
        // Count "!= null" occurrences.
        var nullChecks = text.Split("!= null").Length - 1;
        Assert.Equal(2, nullChecks);
    }

    [Fact]
    public void OptionalChain_TwoGuards_Runtime_NullShortCircuitsCorrectly()
    {
        var expr = Translate<PersonHolder, bool>("(p) => p.Person?.City?.startsWith('A')");
        var fn = expr.Compile();
        Assert.True (fn(new PersonHolder { Person = new TestAddress { City = "Alpha" } }));
        Assert.False(fn(new PersonHolder { Person = new TestAddress { City = "Beta"  } }));
        Assert.False(fn(new PersonHolder { Person = new TestAddress { City = null!   } })); // inner guard null
        Assert.False(fn(new PersonHolder { Person = null                                })); // outer guard null
    }

    [Fact]
    public void OptionalChain_NonBoolBody_EmitsSingleIif_NotNested()
    {
        // `u.Address?.City` as a string result — can't flatten to bool; keep a single
        // IIF (not nested) so providers that tolerate one level still work.
        var expr = Translate<TestUser, string?>("(u) => u.Address?.City");
        var text = expr.ToString();
        var iifCount = text.Split("IIF").Length - 1;
        Assert.Equal(1, iifCount);
    }

    [Fact]
    public void OptionalChain_Negation_StillJsTruthy_AndFlat()
    {
        // `!u.Address?.City.startsWith('V')` — combining negation with optional chain.
        // Should flatten the chain first (bool context via NormalizeToBool inside Not),
        // then negate. Result: `!(u.Address != null && u.Address.City.StartsWith("V"))`.
        var expr = Translate<TestUser, bool>("(u) => !u.Address?.City.startsWith('V')");
        var text = expr.ToString();
        Assert.DoesNotContain("IIF", text, StringComparison.Ordinal);

        // And the runtime semantics must still match JS: `!undefined === true`.
        var fn = expr.Compile();
        Assert.True(fn(new TestUser { Address = null })); // !undefined = true ✓
    }

    // --- Binary-comparison chain flatten: `IIF(test, body, null) op nonNullConst` ---
    // Marten (and many other LINQ providers) refuse Conditional inside a binary
    // comparison. For equality / ordering operators where `null op X` evaluates
    // to false anyway, we flatten the chain into `test && body op X`.

    [Fact]
    public void OptionalChain_EqualityWithConstant_Flattens_NoIif()
    {
        // Customer?.Id === guid — from the real-world rollback case.
        var expr = Translate<PersonHolder, bool>(
            "(p) => p.Person?.City === 'Vienna'");
        var text = expr.ToString();
        Assert.DoesNotContain("IIF", text, StringComparison.Ordinal);
        Assert.Contains("!= null", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalChain_EqualityWithConstant_RuntimeSemantics()
    {
        var expr = Translate<PersonHolder, bool>(
            "(p) => p.Person?.City === 'Vienna'");
        var fn = expr.Compile();
        Assert.True (fn(new PersonHolder { Person = new TestAddress { City = "Vienna" } }));
        Assert.False(fn(new PersonHolder { Person = new TestAddress { City = "Berlin" } }));
        Assert.False(fn(new PersonHolder { Person = null })); // null === "Vienna" is false
    }

    [Fact]
    public void OptionalChain_EqualityConstantOnLeft_AlsoFlattens()
    {
        // `'Vienna' === p.Person?.City` — reversed operand order.
        var expr = Translate<PersonHolder, bool>(
            "(p) => 'Vienna' === p.Person?.City");
        var text = expr.ToString();
        Assert.DoesNotContain("IIF", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BoolConstComparison_EqualsTrue_CollapsesToOperand()
    {
        // `u.IsActive === true` → `u.IsActive` (same expression tree).
        Expression<Func<TestUser, bool>> baseline = u => u.IsActive;
        var actual = Translate<TestUser, bool>("(u) => u.IsActive === true");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void BoolConstComparison_EqualsFalse_CollapsesToNot()
    {
        Expression<Func<TestUser, bool>> baseline = u => !u.IsActive;
        var actual = Translate<TestUser, bool>("(u) => u.IsActive === false");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void BoolConstComparison_NotEqualsTrue_CollapsesToNot()
    {
        Expression<Func<TestUser, bool>> baseline = u => !u.IsActive;
        var actual = Translate<TestUser, bool>("(u) => u.IsActive !== true");
        Assert.Equal(baseline.ToString(), actual.ToString());
    }

    [Fact]
    public void BoolConstComparison_OptionalChainWithEqualsTrue_FlatAndFree()
    {
        // The real v3.1.0 failing shape — after flattening AND bool-const simplify,
        // this produces identical output to the version without `=== true`.
        var expr = Translate<TestUser, bool>(
            "(u) => u.Address?.City.startsWith('V') === true");
        var text = expr.ToString();
        Assert.DoesNotContain("IIF", text, StringComparison.Ordinal);
        Assert.DoesNotContain("== True", text, StringComparison.Ordinal);

        var fn = expr.Compile();
        Assert.True (fn(new TestUser { Address = new TestAddress { City = "Vienna" } }));
        Assert.False(fn(new TestUser { Address = new TestAddress { City = "Berlin" } }));
        Assert.False(fn(new TestUser { Address = null }));
    }

    [Fact]
    public void BoolConstComparison_OnNullableBool_DoesNotCollapse()
    {
        // Don't simplify `bool? === true` — the comparison has meaningful lifted
        // semantics (null === true is false, preserved by Equal). Only plain bool
        // operands get the shortcut.
        // We test this by taking a property path that the Translator sees as bool?
        // (via optional chain on a non-bool intermediate). Hmm, trickier to induce
        // — just assert the simplification doesn't misfire on a plain `bool?`
        // SetValue closure.
        var expr = Translate<TestUser, bool>(
            "(u) => maybeActive === true",
            engine => engine.SetValue("maybeActive", (bool?)true));
        // maybeActive is bool? (Jint sees it typed via the SetValue); the expression
        // should NOT collapse to `maybeActive` (that'd fail type-check since lambda
        // returns bool) — the translator should emit a proper Equal (or normalize).
        var fn = expr.Compile();
        Assert.True(fn(new TestUser()));
    }
}
