using System;
using System.Threading.Tasks;
using Cocoar.JsEval;
using Cocoar.JsEval.Engine;
using Cocoar.JsEval.Module.Common;
using Cocoar.JsEval.TypeScript;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsEval.Tests.TypeScript;

/// <summary>
/// Tests that verify TypeScript features are correctly transpiled to JavaScript
/// and execute properly in the Jint engine.
/// </summary>
public class TypeScriptTranspilationTests
{
    private IServiceProvider ServiceProvider { get; }
    private readonly ITestOutputHelper _output;

    public TypeScriptTranspilationTests(ITestOutputHelper output)
    {
        _output = output;

        var sc = new ServiceCollection();
        sc.AddJsEval(b => b.AddModule<CommonModule>());
        sc.AddTsTranspiler();

        ServiceProvider = sc.BuildServiceProvider();
    }

    private TsTranspiler GetTranspiler() => ServiceProvider.GetRequiredService<TsTranspiler>();
    private JsEngine GetEngine() => ServiceProvider.GetRequiredService<JsEngine>();

    [Fact]
    public async Task TypeAnnotations_AreStrippedAndCodeExecutes()
    {
        var transpiler = GetTranspiler();
        using var jsEngine = GetEngine();

        var ts = @"
const name: string = 'hello';
const count: number = 42;
const flag: boolean = true;
export const result: string = `${name}-${count}-${flag}`;
";
        var js = transpiler.Transpile(ts);
        _output.WriteLine(js);
        Assert.DoesNotContain(": string", js);
        Assert.DoesNotContain(": number", js);
        Assert.DoesNotContain(": boolean", js);

        await jsEngine.ExecuteAsync(js);
        Assert.Equal("hello-42-true", jsEngine.GetValue<string>("result"));
    }

    [Fact]
    public async Task Interface_IsErasedAndObjectWorks()
    {
        var transpiler = GetTranspiler();
        using var jsEngine = GetEngine();

        var ts = @"
interface User { name: string; age: number; active: boolean; }
const user: User = { name: 'Alice', age: 30, active: true };
export const userName: string = user.name;
export const userAge: number = user.age;
";
        var js = transpiler.Transpile(ts);
        Assert.DoesNotContain("interface", js);

        await jsEngine.ExecuteAsync(js);
        Assert.Equal("Alice", jsEngine.GetValue<string>("userName"));
        Assert.Equal(30, jsEngine.GetValue<int>("userAge"));
    }

    [Fact]
    public async Task Enum_TranspilesAndResolvesValues()
    {
        var transpiler = GetTranspiler();
        using var jsEngine = GetEngine();

        var ts = @"
enum Direction { Up, Down, Left, Right }
export const dir: number = Direction.Down;
export const dirRight: number = Direction.Right;
";
        var js = transpiler.Transpile(ts);
        await jsEngine.ExecuteAsync(js);
        Assert.Equal(1, jsEngine.GetValue<int>("dir"));
        Assert.Equal(3, jsEngine.GetValue<int>("dirRight"));
    }

    [Fact]
    public async Task StringEnum_TranspilesCorrectly()
    {
        var transpiler = GetTranspiler();
        using var jsEngine = GetEngine();

        var ts = @"
enum Color { Red = 'RED', Green = 'GREEN', Blue = 'BLUE' }
export const color: string = Color.Green;
";
        var js = transpiler.Transpile(ts);
        await jsEngine.ExecuteAsync(js);
        Assert.Equal("GREEN", jsEngine.GetValue<string>("color"));
    }

    [Fact]
    public async Task GenericFunction_TranspilesAndExecutes()
    {
        var transpiler = GetTranspiler();
        using var jsEngine = GetEngine();

        var ts = @"
function identity<T>(value: T): T { return value; }
export const strResult: string = identity<string>('hello');
export const numResult: number = identity<number>(42);
";
        var js = transpiler.Transpile(ts);
        Assert.DoesNotContain("<T>", js);

        await jsEngine.ExecuteAsync(js);
        Assert.Equal("hello", jsEngine.GetValue<string>("strResult"));
        Assert.Equal(42, jsEngine.GetValue<int>("numResult"));
    }

    [Fact]
    public async Task TypeAlias_IsErased()
    {
        var transpiler = GetTranspiler();
        using var jsEngine = GetEngine();

        var ts = @"
type StringOrNumber = string | number;
const value: StringOrNumber = 'test';
export const result: string = typeof value;
";
        var js = transpiler.Transpile(ts);
        Assert.DoesNotContain("StringOrNumber", js);

        await jsEngine.ExecuteAsync(js);
        Assert.Equal("string", jsEngine.GetValue<string>("result"));
    }

    [Fact]
    public async Task OptionalParams_Work()
    {
        var transpiler = GetTranspiler();
        using var jsEngine = GetEngine();

        var ts = @"
function greet(name: string, greeting?: string): string {
    return `${greeting || 'Hello'}, ${name}!`;
}
export const r1: string = greet('World');
export const r2: string = greet('World', 'Hi');
";
        await jsEngine.ExecuteAsync(transpiler.Transpile(ts));
        Assert.Equal("Hello, World!", jsEngine.GetValue<string>("r1"));
        Assert.Equal("Hi, World!", jsEngine.GetValue<string>("r2"));
    }

    [Fact]
    public async Task DefaultParams_Work()
    {
        var transpiler = GetTranspiler();
        using var jsEngine = GetEngine();

        var ts = @"
function multiply(a: number, b: number = 2): number { return a * b; }
export const r1: number = multiply(5);
export const r2: number = multiply(5, 3);
";
        await jsEngine.ExecuteAsync(transpiler.Transpile(ts));
        Assert.Equal(10, jsEngine.GetValue<int>("r1"));
        Assert.Equal(15, jsEngine.GetValue<int>("r2"));
    }

    [Fact]
    public async Task RestParams_Work()
    {
        var transpiler = GetTranspiler();
        using var jsEngine = GetEngine();

        var ts = @"
function sum(...numbers: number[]): number { return numbers.reduce((acc, n) => acc + n, 0); }
export const total: number = sum(1, 2, 3, 4, 5);
";
        await jsEngine.ExecuteAsync(transpiler.Transpile(ts));
        Assert.Equal(15, jsEngine.GetValue<int>("total"));
    }

    [Fact]
    public async Task TypedArrowFunctions_Work()
    {
        var transpiler = GetTranspiler();
        using var jsEngine = GetEngine();

        var ts = @"
const add = (a: number, b: number): number => a + b;
const toUpper = (s: string): string => s.toUpperCase();
export const sum: number = add(10, 20);
export const upper: string = toUpper('hello');
";
        await jsEngine.ExecuteAsync(transpiler.Transpile(ts));
        Assert.Equal(30, jsEngine.GetValue<int>("sum"));
        Assert.Equal("HELLO", jsEngine.GetValue<string>("upper"));
    }

    [Fact]
    public async Task Class_WithTypedMembers_Works()
    {
        var transpiler = GetTranspiler();
        using var jsEngine = GetEngine();

        var ts = @"
class Calculator {
    private value: number;
    constructor(initial: number) { this.value = initial; }
    add(n: number): Calculator { this.value += n; return this; }
    getResult(): number { return this.value; }
}
const calc = new Calculator(10); //ignore
export const result: number = calc.add(5).add(3).getResult();
";
        await jsEngine.ExecuteAsync(transpiler.Transpile(ts));
        Assert.Equal(18, jsEngine.GetValue<int>("result"));
    }

    [Fact]
    public async Task Destructuring_WithTypes_Works()
    {
        var transpiler = GetTranspiler();
        using var jsEngine = GetEngine();

        var ts = @"
interface Point { x: number; y: number; }
const point: Point = { x: 10, y: 20 };
const { x, y }: Point = point;
export const sum: number = x + y;
";
        await jsEngine.ExecuteAsync(transpiler.Transpile(ts));
        Assert.Equal(30, jsEngine.GetValue<int>("sum"));
    }

    [Fact]
    public async Task TemplateLiterals_WithTypedExpressions()
    {
        using var jsEngine = GetEngine();
        var js = GetTranspiler().Transpile("const n: string = 'TypeScript'; const v: number = 6; export const m: string = `${n} version ${v} is working!`;");
        await jsEngine.ExecuteAsync(js);
        Assert.Equal("TypeScript version 6 is working!", jsEngine.GetValue<string>("m"));
    }

    [Fact]
    public async Task SpreadOperator_Works()
    {
        using var jsEngine = GetEngine();
        var js = GetTranspiler().Transpile("const a: number[] = [1,2,3]; const b: number[] = [4,5,6]; const c: number[] = [...a,...b]; export const len: number = c.length; export const last: number = c[5];");
        await jsEngine.ExecuteAsync(js);
        Assert.Equal(6, jsEngine.GetValue<int>("len"));
        Assert.Equal(6, jsEngine.GetValue<int>("last"));
    }

    [Fact]
    public async Task TypeAssertion_IsErased()
    {
        var transpiler = GetTranspiler();
        using var jsEngine = GetEngine();

        var ts = "const v: any = 'hello'; const l: number = (v as string).length; export const result: number = l;";
        var js = transpiler.Transpile(ts);
        Assert.DoesNotContain(" as ", js);

        await jsEngine.ExecuteAsync(js);
        Assert.Equal(5, jsEngine.GetValue<int>("result"));
    }

    [Fact]
    public async Task NullishCoalescing_And_OptionalChaining()
    {
        using var jsEngine = GetEngine();
        var js = GetTranspiler().Transpile("interface C { timeout?: number; name?: string; } const c: C = {}; export const t: number = c.timeout ?? 30; export const n: number = c.name?.length ?? 0;");
        await jsEngine.ExecuteAsync(js);
        Assert.Equal(30, jsEngine.GetValue<int>("t"));
        Assert.Equal(0, jsEngine.GetValue<int>("n"));
    }

    [Fact]
    public void CompileScript_ProducesValidJavaScript()
    {
        var transpiler = GetTranspiler();

        var ts = @"
interface Animal { name: string; sound: string; }
function describe(animal: Animal): string { return `${animal.name} says ${animal.sound}`; }
const cat: Animal = { name: 'Cat', sound: 'Meow' };
export const description: string = describe(cat);
";
        var js = transpiler.Transpile(ts);
        _output.WriteLine("=== Transpiled Output ===");
        _output.WriteLine(js);

        Assert.DoesNotContain("interface Animal", js);
        Assert.DoesNotContain(": Animal", js);
        Assert.DoesNotContain(": string", js);
        Assert.Contains("function describe", js);
        Assert.Contains("export", js);
    }
}
