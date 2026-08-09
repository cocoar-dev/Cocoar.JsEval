using System;
using System.Collections.Generic;
using Cocoar.JsEval.Engine;
using Xunit;

namespace JsEval.Tests.Engine;

public class JsSandboxTests
{
    // =====================================================================
    // Capability surface
    // =====================================================================

    [Fact]
    public void EmptySandbox_ExposesNoHostCapabilities()
    {
        var sandbox = new JsSandbox();

        sandbox.Execute("""
            const forbidden = [
                'System', 'importNamespace', 'clr', 'fetch', 'require',
                'NewObject', 'console', 'setTimeout', 'setInterval',
                'Atomics', 'SharedArrayBuffer'
            ];

            for (const name of forbidden) {
                if (typeof globalThis[name] !== 'undefined') {
                    throw new Error(name + ' must not be available');
                }
            }

            if (typeof input !== 'undefined') {
                throw new Error('empty sandbox must not contain input');
            }
            """);
    }

    [Fact]
    public void EmptySandbox_BlocksClrTypeAndReflectionPaths()
    {
        var sandbox = new JsSandbox();

        sandbox.Execute("""
            const value = {};
            if (typeof value.GetType !== 'undefined' || typeof value.getType !== 'undefined') {
                throw new Error('GetType must not be available');
            }
            """);
    }

    [Theory]
    [InlineData("eval('1 + 1');")]
    [InlineData("new Function('return 42');")]
    [InlineData("({}).constructor.constructor('return globalThis')();")]
    public void EmptySandbox_DisablesEvalAndFunctionConstructor(string script)
    {
        var sandbox = new JsSandbox();

        Assert.Throws<JsSandboxException>(() => sandbox.Execute(script));
    }

    // =====================================================================
    // Values cross the boundary as data only
    // =====================================================================

    [Fact]
    public void SerializedPoco_CanBeChangedAndReturnedAsNewObject()
    {
        var sandbox = new JsSandbox();
        var original = new SandboxPerson
        {
            Name = " Alice ",
            Age = 17,
            Address = new SandboxAddress { City = "Graz" },
            Tags = ["customer"]
        };

        sandbox.SetValue("person", original);
        sandbox.Execute("""
            person.Name = person.Name.trim();
            person.Age += 1;
            person.Address.City = 'Vienna';
            person.Tags.push('validated');
            """);
        var changed = sandbox.GetValue<SandboxPerson>("person");

        Assert.NotNull(changed);
        Assert.NotSame(original, changed);
        Assert.Equal(" Alice ", original.Name);
        Assert.Equal(17, original.Age);
        Assert.Equal("Graz", original.Address.City);
        Assert.Equal(["customer"], original.Tags);

        Assert.Equal("Alice", changed.Name);
        Assert.Equal(18, changed.Age);
        Assert.Equal("Vienna", changed.Address.City);
        Assert.Equal(["customer", "validated"], changed.Tags);
    }

    [Fact]
    public void SerializedPoco_DoesNotExposeClrIdentityOrMethods()
    {
        var sandbox = new JsSandbox();

        sandbox.SetValue("person", new SandboxPerson { Name = "Alice" });
        sandbox.Execute("""
            if (typeof person.GetType !== 'undefined' ||
                typeof person.getType !== 'undefined' ||
                typeof person.HostMethod !== 'undefined') {
                throw new Error('serialized POCO leaked CLR members');
            }
            person.Name = 'safe';
            """);

        Assert.Equal("safe", sandbox.GetValue<SandboxPerson>("person")?.Name);
    }

    [Fact]
    public void NamedSerializedValues_CanBeSetChangedAndRetrieved()
    {
        var sandbox = new JsSandbox();
        var customer = new SandboxPerson { Name = "Alice", Age = 20 };
        var context = new SandboxContext { MinimumAge = 18, Prefix = "Ms. " };

        sandbox.SetValue("customer", customer);
        sandbox.SetValue("context", context);
        sandbox.SetValue("isValid", false);

        sandbox.Execute("""
            customer.Name = context.Prefix + customer.Name;
            customer.Age += 1;
            isValid = customer.Age >= context.MinimumAge;
            """);

        Assert.Equal("Ms. Alice", sandbox.GetValue<SandboxPerson>("customer")?.Name);
        Assert.Equal(21, sandbox.GetValue<SandboxPerson>("customer")?.Age);
        Assert.Equal(18, sandbox.GetValue<SandboxContext>("context")?.MinimumAge);
        Assert.True(sandbox.GetValue<bool>("isValid"));

        Assert.Equal("Alice", customer.Name);
        Assert.Equal(20, customer.Age);
    }

    [Fact]
    public void NullProperty_ArrivesAsNullRatherThanMissing()
    {
        // A rule must be able to distinguish "the host set this to null" from
        // "the host never set it", so nulls are serialized rather than dropped.
        var sandbox = new JsSandbox();
        sandbox.SetValue("customer", new SandboxNullable { Name = "Alice", MiddleName = null });

        sandbox.Execute("""
            if (!('MiddleName' in customer)) throw new Error('property was dropped');
            if (customer.MiddleName !== null) throw new Error('property was not null');
            """);
    }

    // =====================================================================
    // Isolation between executions
    // =====================================================================

    [Fact]
    public void NamedValuesPersist_ButOtherGlobalsDoNot()
    {
        var sandbox = new JsSandbox();
        sandbox.SetValue("counter", 1);

        sandbox.Execute("counter++; globalThis.leaked = true;");
        sandbox.Execute("""
            if (typeof globalThis.leaked !== 'undefined') {
                throw new Error('non-value state leaked between executions');
            }
            counter++;
            """);

        Assert.Equal(3, sandbox.GetValue<int>("counter"));
    }

    [Fact]
    public void EveryExecution_UsesFreshGlobals()
    {
        var sandbox = new JsSandbox();

        sandbox.Execute("globalThis.leaked = 42;");
        sandbox.Execute("""
            if (typeof globalThis.leaked !== 'undefined') {
                throw new Error('sandbox state leaked between executions');
            }
            """);
    }

    [Theory]
    [InlineData("Object.prototype.polluted = true;")]
    [InlineData("Array.prototype[1] = 'injected';")]
    [InlineData("String.prototype.toUpperCase = () => 'hacked';")]
    public void PrototypePollution_IsRejected(string script)
    {
        // Built-in prototypes are frozen. JSON.stringify reads inherited index
        // properties on sparse arrays, so an unfrozen Array.prototype would let
        // a rule inject values into its own result that it never assigned.
        var sandbox = new JsSandbox();

        Assert.Throws<JsSandboxException>(() => sandbox.Execute(script));
    }

    // =====================================================================
    // Nesting depth — an unbounded structure used to kill the process
    // =====================================================================

    [Fact]
    public void DeeplyNestedResult_IsRejectedInsteadOfCrashingTheProcess()
    {
        // Jint's JSON serializer recurses once per level. Before the depth
        // guard this exact script terminated the host process with an
        // uncatchable StackOverflowException while staying inside every other
        // configured limit — the JSON is only ~24 KB and it runs in milliseconds.
        // This test passing at all is the regression check.
        var sandbox = new JsSandbox();
        sandbox.SetValue("data", new object());

        var ex = Assert.Throws<JsSandboxException>(() => sandbox.Execute("""
            data = {};
            let r = data;
            for (let i = 0; i < 4000; i++) { r.n = {}; r = r.n; }
            """));

        Assert.Contains("nests deeper", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeeplyNestedArray_IsRejected()
    {
        var sandbox = new JsSandbox();
        sandbox.SetValue("data", Array.Empty<int>());

        Assert.Throws<JsSandboxException>(() => sandbox.Execute("""
            data = [];
            let r = data;
            for (let i = 0; i < 500; i++) { const n = []; r.push(n); r = n; }
            """));
    }

    [Fact]
    public void OrdinaryNesting_IsAcceptedAndRoundTrips()
    {
        var sandbox = new JsSandbox();
        sandbox.SetValue("data", new object());

        sandbox.Execute("""
            data = {};
            let r = data;
            for (let i = 0; i < 20; i++) { r.n = { level: i }; r = r.n; }
            """);

        Assert.Contains("\"level\":19", sandbox.GetValueAsJson("data"));
    }

    [Fact]
    public void RejectedExecution_LeavesPreviousValuesIntact()
    {
        var sandbox = new JsSandbox();
        sandbox.SetValue("data", new SandboxPerson { Name = "Alice" });

        Assert.Throws<JsSandboxException>(() => sandbox.Execute("""
            data = {};
            let r = data;
            for (let i = 0; i < 4000; i++) { r.n = {}; r = r.n; }
            """));

        Assert.Equal("Alice", sandbox.GetValue<SandboxPerson>("data")?.Name);
    }

    // =====================================================================
    // A script cannot quietly detach a value from its name
    // =====================================================================

    [Theory]
    [InlineData("let customer = { Name: 'Mallory' };")]
    [InlineData("const customer = { Name: 'Mallory' };")]
    [InlineData("var customer = { Name: 'Mallory' };")]
    public void RedeclaringAValue_ReturnsWhatTheScriptProduced(string script)
    {
        // A top-level let/const shadows the global the value was installed as.
        // Reading that global directly would hand the host the pre-execution
        // value and silently discard the rule's work.
        var sandbox = new JsSandbox();
        sandbox.SetValue("customer", new SandboxPerson { Name = "Alice" });

        sandbox.Execute(script);

        Assert.Equal("Mallory", sandbox.GetValue<SandboxPerson>("customer")?.Name);
    }

    [Theory]
    [InlineData("delete globalThis.customer;")]
    [InlineData("customer = undefined;")]
    public void RemovingAValue_KeepsTheNameAndReadsBackAsNull(string script)
    {
        var sandbox = new JsSandbox();
        sandbox.SetValue("customer", new SandboxPerson { Name = "Alice" });

        sandbox.Execute(script);

        Assert.Equal("null", sandbox.GetValueAsJson("customer"));
        Assert.Null(sandbox.GetValue<SandboxPerson>("customer"));

        // The name still exists, so a later run can repopulate it.
        sandbox.Execute("customer = { Name: 'Restored' };");
        Assert.Equal("Restored", sandbox.GetValue<SandboxPerson>("customer")?.Name);
    }

    [Fact]
    public void ValueLeftNonSerializable_IsReportedNotSilentlyDropped()
    {
        var sandbox = new JsSandbox();
        sandbox.SetValue("customer", new SandboxPerson { Name = "Alice" });

        var ex = Assert.Throws<JsSandboxException>(() => sandbox.Execute("customer = function () {};"));

        Assert.Contains("JSON-serializable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================
    // Value names
    // =====================================================================

    [Theory]
    [InlineData("undefined")]   // non-writable global: the value would vanish
    [InlineData("NaN")]         // non-writable global: reads back as null
    [InlineData("Infinity")]
    [InlineData("globalThis")]  // would replace the global object
    [InlineData("if")]          // reserved word: not bindable as an identifier
    [InlineData("class")]
    [InlineData("2bad")]        // not an identifier: only reachable via globalThis[...]
    [InlineData("a b")]
    [InlineData("a-b")]
    public void InvalidValueNames_AreRejected(string name)
    {
        var sandbox = new JsSandbox();

        Assert.Throws<ArgumentException>(() => sandbox.SetValue(name, 1));
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("_private")]
    [InlineData("$dollar")]
    [InlineData("item2")]
    public void ValidValueNames_AreAccepted(string name)
    {
        var sandbox = new JsSandbox();
        sandbox.SetValue(name, 41);

        sandbox.Execute($"{name} += 1;");

        Assert.Equal(42, sandbox.GetValue<int>(name));
    }

    // =====================================================================
    // Limits and the exception contract
    // =====================================================================

    [Theory]
    [InlineData("while (true) { }")]
    [InlineData("new Array(10001);")]
    [InlineData("(function f() { return f(); })();")]
    public void RunawayScripts_AreStoppedAsSandboxExceptions(string script)
    {
        var sandbox = new JsSandbox(new JsSandboxOptions
        {
            ExecutionTimeout = TimeSpan.FromMilliseconds(100),
            MaxStatements = 1_000
        });

        Assert.Throws<JsSandboxException>(() => sandbox.Execute(script));
    }

    [Fact]
    public void OversizedOutput_IsReportedAsASandboxException()
    {
        var sandbox = new JsSandbox(new JsSandboxOptions { MaxOutputBytes = 1024 });
        sandbox.SetValue("data", "small");

        Assert.Throws<JsSandboxException>(() => sandbox.Execute("data = 'y'.repeat(5000);"));
        Assert.Equal("\"small\"", sandbox.GetValueAsJson("data"));
    }

    [Fact]
    public void HostMisuse_IsNotDisguisedAsAScriptFailure()
    {
        // Oversized input and an oversized script are bugs in the calling code,
        // so they stay ArgumentException rather than becoming a script failure.
        var sandbox = new JsSandbox(new JsSandboxOptions { MaxInputBytes = 64, MaxScriptBytes = 32 });

        Assert.Throws<ArgumentException>(() => sandbox.SetValue("data", new string('x', 500)));
        Assert.Throws<ArgumentException>(() => sandbox.Execute(new string(' ', 100) + "var x = 1;"));
    }

    [Fact]
    public void UnknownValueName_ThrowsKeyNotFound()
    {
        var sandbox = new JsSandbox();

        Assert.Throws<KeyNotFoundException>(() => sandbox.GetValueAsJson("neverSet"));
    }

    public sealed class SandboxPerson
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public SandboxAddress Address { get; set; } = new();
        public List<string> Tags { get; set; } = [];

        public string HostMethod() => "must never be visible";
    }

    public sealed class SandboxAddress
    {
        public string City { get; set; } = "";
    }

    public sealed class SandboxContext
    {
        public int MinimumAge { get; set; }
        public string Prefix { get; set; } = "";
    }

    public sealed class SandboxNullable
    {
        public string Name { get; set; } = "";
        public string? MiddleName { get; set; }
    }
}
