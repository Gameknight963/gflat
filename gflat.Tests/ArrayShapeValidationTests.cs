using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ArrayShapeValidationTests
{
    [Theory]
    [InlineData("int[] Make() { return [1, 2]; }")]
    [InlineData("int main() { int[] values; return 0; }")]
    [InlineData("struct S { public int[] values; }")]
    [InlineData("class S { public int[] values; }")]
    [InlineData("int[] values;")]
    [InlineData("void F(int[][2] values) {}")]
    [InlineData("void F(ref int[][2] values) {}")]
    [InlineData("int[][2] Make() { return [[1, 2], [3, 4]]; }")]
    [InlineData("alias Buffer = int[]; Buffer Make() { return [1, 2]; }")]
    [InlineData("int main() { int[][] values = [[1, 2], [3, 4]]; return 0; }")]
    [InlineData("extern int[] Make();")]
    [InlineData("int[]()* callback;")]
    [InlineData("int main() { int[]* pointer = ((nuint)1).reinterpret<int[]*>(); *pointer; return 0; }")]
    [InlineData("int main() { int[]* pointer = ((nuint)1).reinterpret<int[]*>(); pointer[0]; return 0; }")]
    [InlineData("int main() { int[]* pointer = ((nuint)1).reinterpret<int[]*>(); pointer + 1; return 0; }")]
    [InlineData("int main() { default(int[]); return 0; }")]
    [InlineData("int main() { sizeof(int[]); return 0; }")]
    public void ArrayValueStorageRequiresEveryBound(string source)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("size", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OuterBoundsAreInferredConsistentlyForLocalGlobalAndInstanceStorage()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            int[] globalNumbers = [20, 22];
            char[] globalText = "ok";
            struct Storage {
                public int[] numbers = [20, 22];
                public char[] text = "ok";
            }
            int main() {
                int[] numbers = [20, 22];
                char[] text = "ok";
                Storage value = new Storage();
                if (sizeof(numbers) != 8 || sizeof(text) != 3) return 1;
                if (sizeof(globalNumbers) != 8 || sizeof(globalText) != 3) return 2;
                if (sizeof(Storage) != 12) return 3;
                if (value.numbers[0] + value.numbers[1] != 42 || value.text[2] != '\0') return 4;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void IncompleteArrayTypesRemainAvailableForDescriptionsAndOpaquePointers()
        => Compiler.Check("""
            int main() { typeof(int[]); int[]*? storage = null; return 0; }
            """);

    [Fact]
    public void InferredStaticAndConstantCharacterArraysHaveInlineStorage()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            struct Storage {
                public static int[] Numbers = [20, 22];
                public const char[] Text = "ok";
            }
            int main() {
                if (Storage::Numbers[0] + Storage::Numbers[1] != 42) return 1;
                if (Storage::Text[0] != 'o' || Storage::Text[2] != '\0') return 2;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void InferredInstanceArraysKeepTheirShapeThroughClassInheritance()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            class Base { public char[] Text = "ok"; }
            class Derived : Base { public int Value = 42; }
            int main() {
                Derived value = new Derived();
                if (value.Text[0] != 'o' || value.Text[2] != '\0' || value.Value != 42) return 1;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void InferredFieldLayoutIsKnownBeforeEarlierConstantInitializers()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            const int size = sizeof(Storage);
            struct Storage { public int[] Values = [1, 2, 3]; }
            int main() { if (size != 12 || sizeof(Storage) != size) return 1; return 0; }
            """).ExitCode);
}
