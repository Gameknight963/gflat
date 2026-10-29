using gflat.diagnostics;

namespace gflat.Tests;

public sealed class InterfacePointerArithmeticTests
{
    private const string Declarations = """
        namespace Lib { interface I { int Read(); } }
        alias View = Lib::I;
        """;
    private const string Expected = "Pointer arithmetic is not allowed on interface pointers; use a pointer to interface-pointer storage for array traversal";

    public static IEnumerable<object[]> RejectedCases()
    {
        foreach (string type in new[] { "Lib::I*", "View*", "readonly(Lib::I)*" })
        foreach (string expression in new[] { "value+1", "1+value", "value-1", "value-value", "++value", "value++", "--value", "value--", "value+=1", "value-=1" })
            yield return [Declarations + $" void Check({type} value){{{expression};}} int main()=>0;"];
    }

    [Theory]
    [MemberData(nameof(RejectedCases))]
    public void InterfacePointeeArithmeticProducesADiagnostic(string source)
    {
        var diagnostics = Compiler.Analyze([new SourceFile("interface-pointer-arithmetic.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(Expected, error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ArraysOfInterfacePointersKeepOrdinaryStorageTraversal(int optimization)
    {
        const string source = Declarations + """
            struct Item : Lib::I {
                public int Id;
                public Item(int id) { Id=id; }
                public int Read()=>Id;
            }
            int main() {
                Item first = new(11); Item second = new(22);
                Lib::I*[2] slots = [&first, &second];
                Lib::I** begin = &slots[0]; Lib::I** cursor = begin;
                if (cursor[0].Read()!=11 || cursor[1].Read()!=22) return 1;
                cursor++;
                if (cursor-begin!=1l || (*cursor).Read()!=22) return 2;
                --cursor;
                if (cursor!=begin || (*cursor).Read()!=11) return 3;
                cursor+=2;
                if (cursor-begin!=2l) return 4;
                cursor-=1;
                if ((*cursor).Read()!=22) return 5;
                Lib::I** previous = cursor-1;
                if (previous!=begin || (*previous).Read()!=11) return 6;
                if ((1+begin)[0].Read()!=22) return 7;
                return 0;
            }
            """;
        string ir = Compiler.Emit([new SourceFile("interface-pointer-storage.gf", source)],
            options: new() { NoStdLib=true, InternalizeFunctions=true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
