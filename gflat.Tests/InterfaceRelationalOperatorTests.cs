using gflat.diagnostics;

namespace gflat.Tests;

public sealed class InterfaceRelationalOperatorTests
{
    private const string Declarations = """
        namespace Lib { interface I { int Read(); } }
        alias View = Lib::I;
        alias GC = TestCollector;
        static class TestCollector {
            public static void* Allocate(nuint size, nuint alignment) => (void*)1;
            public static void ReferenceAdded(void* slot) nothrow {}
            public static void ReferenceRemoved(void* slot) nothrow {}
        }
        """;

    public static IEnumerable<object[]> RejectedCases()
    {
        (string Parameters, string Left, string Right, string LeftName, string RightName)[] forms = [
            ("Lib::I* left, Lib::I* right", "left", "right", "Lib::I*", "Lib::I*"),
            ("Lib::I*? left, Lib::I*? right", "left", "right", "Lib::I*?", "Lib::I*?"),
            ("readonly(Lib::I)* left, readonly(Lib::I)* right", "left", "right", "readonly(Lib::I)*", "readonly(Lib::I)*"),
            ("View* left, View* right", "left", "right", "Lib::I*", "Lib::I*"),
            ("Lib::I^ left, Lib::I^ right", "left", "right", "Lib::I^", "Lib::I^"),
            ("Lib::I^? right", "null", "right", "null", "Lib::I^?"),
            ("Lib::I*? left", "left", "null", "Lib::I*?", "null")
        ];
        foreach (string operation in new[] { "<", "<=", ">", ">=" })
        foreach (var form in forms)
            yield return [Declarations + $" bool Before({form.Parameters})=>{form.Left}{operation}{form.Right}; int main()=>0;",
                $"Operator '{operation}' cannot be applied to operands of type '{form.LeftName}' and '{form.RightName}'"];
    }

    [Theory]
    [MemberData(nameof(RejectedCases))]
    public void OrderedInterfaceAddressesProduceTypeDiagnostics(string source, string expected)
    {
        var diagnostics = Compiler.Analyze([new SourceFile("interface-order.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("GF1002", error.Descriptor.Id);
        Assert.Equal(expected, error.Message);
        Assert.DoesNotContain("$", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InterfaceEqualityAndSameArrayRawPointerOrderingRemainUsable(int optimization)
    {
        const string source = Declarations + """
            struct Item : Lib::I { public int Id; public Item(int id) { Id = id; } public int Read()=>Id; }
            int main() {
                Item first = new(1); Item second = new(2);
                Lib::I* a = &first; Lib::I* same = &first; Lib::I* other = &second;
                if (!(a == same) || a != same || a == other || !(a != other)) return 1;
                Lib::I*? absent = null;
                if (absent != null || !(absent == null) || null != absent || !(null == absent)) return 2;
                Lib::I^? managedView = null;
                if (managedView != null || !(managedView == null) || !(managedView == managedView)) return 3;
                int[2] storage = [1,2]; int* low = &storage[0]; int* high = &storage[1];
                if (!(low < high) || !(low <= high) || !(high > low) || !(high >= low)) return 4;
                if (!(low <= low) || !(low >= low) || low < low || low > low) return 5;
                return 0;
            }
            """;
        string ir = Compiler.Emit([new SourceFile("interface-order-controls.gf", source)],
            options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
