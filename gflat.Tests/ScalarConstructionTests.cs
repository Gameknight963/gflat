using gflat.CompileExceptions;
using Xunit;

namespace gflat.Tests;

public class ScalarConstructionTests
{
    [Fact]
    public void ValueHeapAndPlacementUseTheSameScalarInitializerRules()
    {
        var result = CompilerTestHelper.Run("""
            int main() {
                int value = new int(42);
                int zero = new int();
                int* heap = new* int(value);
                int* placed = new* int(*heap + 1) at &value;
                int answer = *placed + zero + *heap;
                delete heap;
                return answer - 85;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ScalarValueAndPlacementNeedNoStandardLibrary()
    {
        string ir = Compiler.Emit("int main() { int value = new int(41); new* int(42) at &value; return value; }",
            options: new() { NoStdLib = true });
        Assert.DoesNotContain("@gflat$Memory$Allocate", ir);
        Assert.DoesNotContain("@__gflat_eh_", ir);
    }

    [Theory]
    [InlineData("int main() { int x = new int(1, 2); return 0; }")]
    [InlineData("int main() { int x = new int(true); return 0; }")]
    [InlineData("int main() { int x = 0; new* int(1) at (readonly(int)*)&x; return 0; }")]
    public void InvalidScalarConstructionProducesSourceDiagnostics(string source)
        => Assert.Throws<TypeCheckException>(() => Compiler.Emit(source));
}
