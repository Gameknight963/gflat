using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ConstantAddressTests
{
    [Theory]
    [InlineData("int", "42")]
    [InlineData("byte", "42")]
    [InlineData("long", "42l")]
    [InlineData("extralong", "42")]
    [InlineData("float", "3.5f")]
    [InlineData("bool", "true")]
    [InlineData("char", "'x'")]
    public void ScalarConstantAddressesPreserveTheirStorageType(string type, string value)
        => Assert.Equal(0, CompilerTestHelper.Run(
            "const " + type + " Number=" + value + "; int main() { if (&Number != &Number || *(&Number) != " + value + ") return 1; return 0; }").ExitCode);

    [Fact]
    public void LocalConstantsRetainDeclarationIdentityAcrossNestedScopes()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            int main() {
                const int number = 42;
                const int other = 42;
                const int sameValue = number;
                const byte narrow = 42;
                readonly(int)* original = &number;
                if (original != &number || original == &other) return 1;
                if (original == &sameValue || *(&sameValue) != 42) return 4;
                if ((readonly(void)*)original == (readonly(void)*)&narrow || *(&narrow) != 42) return 5;
                {
                    const int number = 42;
                    if (&number == original || &number != &number) return 2;
                }
                if (&number != original || *original != 42) return 3;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void QualifiedAndUnqualifiedConstantsShareTheirDeclaredStorage()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            using Values;
            namespace Values { public const int Number = 42; }
            struct Constants { public const int Number = 42; public static readonly(int)* Address() => &Number; }
            int main() {
                if (&Number != &Values::Number || *(&Values::Number) != 42) return 1;
                if (Constants::Address() != &Constants::Number) return 2;
                if (&Number == &Constants::Number) return 3;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void ConstantArraysHaveOneStableStoragePerDeclaration()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            const int[2] Numbers = [20, 22];
            const int[2] Other = [20, 22];
            int main() {
                if (&Numbers != &Numbers || &Numbers == &Other) return 1;
                if ((*(&Numbers))[0] != 20 || (*(&Numbers))[1] != 22) return 2;
                readonly(int)* first = Numbers;
                if (first != &(*(&Numbers))[0]) return 3;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void ConstantPointerAddressRefersToThePointerSlot()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            const immutable(char*) Text = "hello";
            int main() { if (&Text != &Text || (*(&Text))[0] != 'h') return 1; return 0; }
            """).ExitCode);

    [Fact]
    public void ConstantAddressRemainsReadonly()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "const int Number=42; int main(){ *(&Number)=1; return 0; }"));

    [Fact]
    public void QualifiedEnumMemberCanBeMaterializedAsATemporary()
        => Assert.Equal(42, CompilerTestHelper.Run(
            "enum Codes { Value=42 } int main(){ Codes* pointer=&Codes::Value; return (int)*pointer; }").ExitCode);
}
