using gflat.comptime;
using gflat.CompileExceptions;

namespace gflat.Tests;

public class AlignmentTests
{
    [Theory]
    [InlineData("char", 1)]
    [InlineData("short", 2)]
    [InlineData("int", 4)]
    [InlineData("double", 8)]
    [InlineData("nuint", 8)]
    [InlineData("extralong", 16)]
    [InlineData("int*", 8)]
    [InlineData("int^", 8)]
    [InlineData("int(int)*", 8)]
    [InlineData("readonly(int)[3]", 4)]
    [InlineData("void", 1)]
    public void AlignmentIsAnIntegerCompileTimeConstant(string type, int expected)
    {
        var (_, checker) = Compiler.Check($"const int A = alignof({type}); int main() => A;");
        Assert.True(checker.TryGetConstValueByName("A", out var value));
        Assert.Equal(expected, Assert.IsType<ConstValue.Integer>(value).Value);
    }

    [Fact]
    public void LayoutQueriesAgreeWithNativeFieldOffsetsAndArrayStride()
    {
        var result = CompilerTestHelper.Run("""
            class Wide { public extralong value; }
            struct Outer { public char prefix; public Wide value; public char tail; }
            enum Small : short { A }
            alias Number = long;
            const int Alignment() => alignof(Number);
            const int A = Alignment();
            int Measure<T>() => alignof(T);
            int main() {
                Outer[2] items;
                if (alignof(Wide) != 16 || alignof(Outer) != 16) return 1;
                if ((nuint)&items[0].value - (nuint)&items[0] != (nuint)16) return 2;
                if ((nuint)&items[1] - (nuint)&items[0] != (nuint)sizeof(Outer)) return 3;
                if (A != 8 || Measure<short>() != 2 || alignof(Small) != 2) return 4;
                int[alignof(double)] buffer;
                if (sizeof(buffer) != 32 || alignof(buffer) != 4) return 5;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void InterfaceReferencesHavePointerAlignmentButTwoWordSize()
    {
        Assert.Equal(0, CompilerTestHelper.Run("interface I {} int main() { if (alignof(I*) != alignof(void*) || sizeof(I*) != 2 * sizeof(void*)) return 1; return 0; }").ExitCode);
    }

    [Fact]
    public void UnknownAlignmentTypeIsDiagnosed()
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Check("int main() => alignof(Missing);"));
    }
}
