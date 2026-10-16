using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class EnumConstantTests
{
    [Theory]
    [InlineData("uint", "2147483648u", "2147483648u")]
    [InlineData("ulong", "18446744073709551615ul", "18446744073709551615ul")]
    [InlineData("ulong", "0xfffffffffffffffful", "18446744073709551615ul")]
    [InlineData("long", "-9223372036854775808l", "-9223372036854775808l")]
    public void SuffixedIntegerMembersPreserveUnderlyingBits(string type, string literal, string expected)
        => Assert.Equal(0, CompilerTestHelper.Run($$"""
            enum Code : {{type}} { Value = {{literal}} }
            const {{type}} folded = ({{type}})Code::Value;
            int main() { Code value = Code::Value; if (({{type}})value != {{expected}} || folded != ({{type}})value) return 1; return 0; }
            """).ExitCode);

    [Theory]
    [InlineData("byte", "256")]
    [InlineData("sbyte", "128")]
    [InlineData("uint", "-1")]
    [InlineData("long", "9223372036854775808ul")]
    [InlineData("ulong", "18446744073709551616ul")]
    [InlineData("byte", "255, Next")]
    [InlineData("extralong", "1")]
    public void UnrepresentableMembersProduceSourceDiagnostics(string type, string value)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check($"enum Code : {type} {{ Value = {value} }}"));

    [Fact]
    public void NamespaceAliasesCanSpecifyTheUnderlyingType()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            namespace Codes {
                alias Storage = ushort;
                enum Code : Storage { Value = 65535 }
            }
            int main() { if ((uint)Codes::Code::Value != 65535u || sizeof(Codes::Code) != 2) return 1; return 0; }
            """).ExitCode);

    [Fact]
    public void DuplicateValuesAndUnsignedCrossEnumReferencesRemainValid()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            namespace Outer {
                enum First : ulong { Max = 18446744073709551615ul, Alias = Max }
                namespace Inner { enum Second : ulong { Same = First::Max } }
            }
            int main() {
                if (Outer::First::Max != Outer::First::Alias) return 1;
                if ((ulong)Outer::Inner::Second::Same != 18446744073709551615ul) return 2;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void TypeAliasesCanQualifyEnumMembers()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            enum Code { Value = 42 }
            alias Alias = Code;
            int main() { if ((int)Alias::Value != 42) return 1; return 0; }
            """).ExitCode);

    [Fact]
    public void QualifiedEnumAliasesAndOptionalDefaultsPreserveEnumIdentity()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            namespace Codes {
                enum Code : ulong { Max = 18446744073709551615ul }
                alias Alias = Code;
                ulong Read(Alias value = Alias::Max) => (ulong)value;
            }
            int main() {
                if ((ulong)Codes::Alias::Max != 18446744073709551615ul) return 1;
                if (Codes::Read() != 18446744073709551615ul) return 2;
                return 0;
            }
            """).ExitCode);

    [Fact]
    public void LocalEnumsUseTheSameUnderlyingAliasesAndConstantRules()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            int main() {
                alias Storage = ulong;
                enum Code : Storage { Max = 18446744073709551615ul, Same = Max }
                const ulong folded = (ulong)Code::Same;
                if (folded != (ulong)Code::Max || folded != 18446744073709551615ul) return 1;
                return 0;
            }
            """).ExitCode);

    [Theory]
    [InlineData("enum Code { Same = 1, Same = 2 }")]
    [InlineData("enum Code { Value = 0x10000000000000000ul }")]
    [InlineData("enum Code { Value = 1 / 0 }")]
    [InlineData("enum Code { Value = !0 }")]
    [InlineData("enum First { Value = 1 } enum Second { Value = First.Value }")]
    public void InvalidConstantMembersProduceSourceDiagnostics(string source)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
}
