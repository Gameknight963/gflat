using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class BuiltinOperatorValidationTests
{
    [Theory]
    [InlineData("struct Value { public int n; }", "Value a = default; Value b = default;", "a + b")]
    [InlineData("struct Value { public int n; }", "Value a = default; Value b = default;", "a * b")]
    [InlineData("struct Value { public int n; }", "Value a = default; Value b = default;", "a == b")]
    [InlineData("", "int[2] a = [1, 2]; int[2] b = [1, 2];", "a == b")]
    [InlineData("", "bool a = true; bool b = false;", "a + b")]
    [InlineData("", "bool a = true; bool b = false;", "a / b")]
    public void UnsupportedBuiltinOperandsProduceDiagnostics(string declarations, string locals, string expression)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(declarations + " int main() { " + locals + expression + "; return 0; }"));

    [Theory]
    [InlineData("struct Value { public int n; }", "Value a = default; Value b = default;", "a += b")]
    [InlineData("struct Value { public int n; }", "Value a = default; Value b = default;", "a *= b")]
    [InlineData("", "bool a = true; bool b = false;", "a += b")]
    [InlineData("", "bool a = true; bool b = false;", "a /= b")]
    public void UnsupportedCompoundOperandsProduceDiagnostics(string declarations, string locals, string expression)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(declarations + " int main() { " + locals + expression + "; return 0; }"));

    [Fact]
    public void ScalarPointerEnumAndDeclaredOperatorsRemainUsable()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            enum Flags : byte { None = 0, One = 1, Two = 2 }
            struct Number {
                public int value;
                public Number(int n) { value = n; }
                public static Number operator +(Number a, Number b) => new Number(a.value + b.value);
                public static bool operator ==(Number a, Number b) => a.value == b.value;
            }
            int main() {
                Number a = new Number(20); Number b = new Number(22); a += b;
                if (!(a == new Number(42))) return 1;
                Flags bits = Flags::One | Flags::Two;
                if ((byte)bits != 3) return 2;
                if (!(true & true) || true ^ true || false | false) return 3;
                int[2] values = [1, 2]; int* pointer = values;
                pointer += 1;
                if (*pointer != 2 || pointer - 1 != &values[0]) return 4;
                nint count = 2; count *= 21;
                if (count != 42) return 5;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void PropertyCompoundAssignmentUsesTheSameOperatorRules()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            struct Value { public bool Enabled { get; set; } }
            int main() { Value value = default; value.Enabled += true; return 0; }
            """));
}
