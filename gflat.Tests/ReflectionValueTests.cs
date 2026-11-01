using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ReflectionValueTests
{
    [Fact]
    public void ExpressionTypesAndNestedValuesRemainDistinct()
    {
        Check("""
            int main()
            {
                const int a = 1;
                const int b = 2;
                const reflection sum = reflect(a + b);
                const reflection nested = reflect(reflect(int));
                static if (sum != reflect(3)) { missingSum(); }
                static if (typeof(a + b) != reflect(int)) { missingType(); }
                static if (typeof(nested) != reflect(reflection)) { missingReflectionType(); }
                static if (nested == reflect(int)) { missingLevel(); }
                return 0;
            }
            """);
    }

    [Theory]
    [InlineData("typeof(int)")]
    [InlineData("reflect(value + 1)")]
    public void TypeQueriesRejectTypeOperandsAndValueReflectionRejectsRuntimeInputs(string expression)
    {
        Assert.Throws<TypeCheckException>(() => Check($"int main() {{ int value = 1; const reflection info = {expression}; return 0; }}"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FunctionsCanReturnTypesForDirectSplicingAndAliases(int optimization)
    {
        Run("""
            const reflection Choose(bool wide)
            {
                if (wide) return reflect(long);
                return reflect(int);
            }
            alias Number = [:Choose(true):];
            int main()
            {
                [:Choose(false):] value = 42;
                Number wide = value;
                if (sizeof(Number) != 8 || sizeof(value) != 4) return 1;
                return (int)wide - 42;
            }
            """, optimization);
    }

    [Fact]
    public void PrivateFieldsRemainInspectableAndHaveAccurateOffsets()
    {
        Check("""
            struct Packet
            {
                private byte flag;
                public int count;
                public static int Shared;
            }
            int main()
            {
                const reflection hidden = reflect(Packet::flag);
                static if (!hidden.IsPrivate || hidden.Offset != 0) { invalidHidden(); }
                static if (reflect(Packet::count).Offset != 4) { invalidOffset(); }
                int instanceCount = 0;
                static foreach (reflection field in reflect(Packet).Fields) { instanceCount++; }
                int staticCount = 0;
                static foreach (reflection field in reflect(Packet).StaticFields) { staticCount++; }
                return instanceCount + staticCount - 3;
            }
            """);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void StaticExpansionUsesEachActualFieldType(int optimization)
    {
        Run("""
            struct Packet { public int count; public long extra; }
            int main()
            {
                Packet packet = new();
                packet.count = 12;
                packet.extra = 30;
                long total = 0;
                static foreach (reflection field in reflect(Packet).Fields)
                {
                    total += packet.[:field:];
                    static if (field.Type == reflect(int))
                    {
                        const int width = sizeof([:field.Type:]);
                        static if (width != 4) { invalidWidth(); }
                    }
                    else
                    {
                        static if (field.Type != reflect(long)) { invalidFieldType(); }
                    }
                }
                return (int)total - 42;
            }
            """, optimization);
    }

    [Fact]
    public void SplicingDoesNotGrantPrivateAccess()
    {
        var error = Assert.Throws<TypeCheckException>(() => Check("""
            struct Packet { private int value; }
            int main()
            {
                Packet packet = new();
                const reflection field = reflect(Packet::value);
                return packet.[:field:];
            }
            """));
        Assert.Contains("private", error.Message);
    }

    private static void Check(string source) => Compiler.Check(source, options: new() { NoStdLib = true });
    private static void Run(string source, int optimization)
    {
        string ir = Compiler.Emit(source, options: new() { NoStdLib = true, InternalizeFunctions = true });
        CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
