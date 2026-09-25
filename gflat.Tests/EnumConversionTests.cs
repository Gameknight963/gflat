using gflat.CompileExceptions;

namespace gflat.Tests;

public class EnumConversionTests
{
    private const string Types = "enum A { Zero = 0, One = 1 } enum B { One = 1 } ";

    [Theory]
    [InlineData("A a = 1; return 0;")]
    [InlineData("A a = B::One; return 0;")]
    [InlineData("int n = A::One; return n;")]
    [InlineData("return A::One;")]
    [InlineData("if (A::One == 1) return 1; return 0;")]
    [InlineData("if (A::One == B::One) return 1; return 0;")]
    public void ImplicitEnumConversionsAreRejected(string body)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(Types + "int main() { " + body + " }"));

    [Fact]
    public void ExplicitCastsDefaultAndFlagsWork()
        => Assert.Equal(42, CompilerTestHelper.Run(Types + """
            int main() { A zero = default; A a = (A)40; B b = (B)a;
                A flags = A::One | A::Zero; return (int)b + (int)flags + (int)((zero ^ A::One) & A::One); }
            """).ExitCode);

    [Fact]
    public void OverloadsPreserveEnumIdentity()
        => Assert.Equal(42, CompilerTestHelper.Run(Types + "int F(int n) => 1; int F(A a) => 42; int main() => F(A::One);").ExitCode);
}
