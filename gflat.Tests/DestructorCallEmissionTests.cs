using System.Text.RegularExpressions;

namespace gflat.Tests;

public sealed class DestructorCallEmissionTests
{
    [Fact]
    public void DestructorDispatchCallsKnownNonUnwindingEntriesWithoutInvokeScaffolding()
    {
        string ir = Compiler.Emit("""
            extern void Observe(int value) nothrow;
            interface IMarker
            {
                int Marker();
                implementation int { public int Marker() => 0; }
            }
            struct S : IMarker
            {
                public int Marker() => 1;
                ~S() { Observe(1); }
            }
            class C : IMarker
            {
                public int Marker() => 2;
                ~C() { Observe(2); }
            }
            class V : IMarker
            {
                public int Marker() => 3;
                public virtual ~V() { Observe(3); }
            }
            class D : V
            {
                ~D() { Observe(4); }
            }
            // These boundaries force the old default-unwind call path to emit
            // invoke even though the called destructor definition is nounwind.
            void DestroyStruct(S* value) nothrow { value.~S(); }
            void DestroyClass(C* value) nothrow { value.~C(); }
            void DestroyVirtual(V* value) nothrow { value.~V(); }
            void DestroyInterface(IMarker* value) nothrow { value.~IMarker(); }
            int main() => 0;
            """, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = false });

        var definitions = Regex.Matches(ir,
            @"(?ms)^(?<header>define[^@\r\n]*@(?<name>[^\s(]+)[^\r\n]*\{)\r?\n(?<body>.*?)^\}")
            .ToDictionary(match => match.Groups["name"].Value);
        foreach (string name in new[] { "DestroyStruct", "DestroyClass", "DestroyVirtual", "DestroyInterface" })
        {
            Assert.True(definitions.TryGetValue("gflat$" + name, out var definition), "Missing probe " + name);
            string body = definition!.Groups["body"].Value;
            Assert.DoesNotContain("invoke ", body);
            Assert.DoesNotContain("personality", definition.Groups["header"].Value);
        }
        Assert.Contains("call void @gflat$S$dtor(", definitions["gflat$DestroyStruct"].Groups["body"].Value);
        // Class destruction uses its destructor slot, including C's implicitly
        // supplied slot. The derived-to-base call below covers direct class dispatch.
        Assert.Matches(@"\bcall void %[^\s(]+\(", definitions["gflat$DestroyClass"].Groups["body"].Value);
        Assert.Matches(@"\bcall void %[^\s(]+\(", definitions["gflat$DestroyVirtual"].Groups["body"].Value);
        Assert.Matches(@"\bcall void %[^\s(]+\(", definitions["gflat$DestroyInterface"].Groups["body"].Value);

        var destructors = definitions.Where(pair => pair.Key.EndsWith("$dtor", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(destructors);
        foreach (var destructor in destructors)
            Assert.Matches(@"\bnounwind\b", destructor.Value.Groups["header"].Value);
        // Derived destruction must call the base destructor without adding its
        // own redundant invoke path, even inside a terminating boundary.
        Assert.Contains("call void @gflat$V$dtor(", definitions["gflat$D$dtor"].Groups["body"].Value);
        Assert.DoesNotContain("invoke ", definitions["gflat$D$dtor"].Groups["body"].Value);

        var thunks = definitions.Where(pair => pair.Key.EndsWith("$interface_destroy", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(thunks);
        Assert.Contains(thunks, pair => pair.Key == "gflat$int$interface_destroy");
        foreach (var thunk in thunks)
        {
            Assert.Matches(@"\bnounwind\b", thunk.Value.Groups["header"].Value);
            Assert.DoesNotContain("invoke ", thunk.Value.Groups["body"].Value);
        }
    }
}
