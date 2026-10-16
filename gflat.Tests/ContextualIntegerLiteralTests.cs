using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ContextualIntegerLiteralTests
{
    [Fact]
    public void InitializersAssignmentsReturnsAndCompoundAssignmentsPreserveLiteralValues()
    {
        var result = CompilerTestHelper.Run("""
            long Signed() => 0xffffffff;
            ulong Unsigned() => 0xffffffff;
            nuint Native() => 0xffffffff;
            int main() {
                long signed = 0xffffffff;
                ulong unsigned = 0xffffffff;
                nuint native = 0xffffffff;
                if (signed != 4294967295l || unsigned != 4294967295ul || native != 4294967295ul) return 1;
                if (Signed() != signed || Unsigned() != unsigned || Native() != native) return 2;
                native = 4294967295;
                if (native != 4294967295ul) return 3;
                native = 0;
                native += 0xffffffff;
                if (native != 4294967295ul) return 4;
                int delta = 1;
                signed = 0;
                signed += delta;
                if (signed != 1l) return 5;
                nint minimum = -9223372036854775808l;
                if (minimum != (nint)(-9223372036854775807l - 1)) return 6;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void CallsConstructorsPropertiesAndDefaultsUseTheirDeclaredParameterWidths()
    {
        var result = CompilerTestHelper.Run("""
            nuint Echo(nuint value) => value;
            nuint Default(nuint value = 0xffffffff) => value;
            nuint Maximum(nuint value = 0xfffffffffffffffful) => value;
            interface IValue { nuint Echo(nuint value); }
            struct Value : IValue {
                private nuint stored = 0xffffffff;
                public Value(nuint value) { stored = value; }
                public nuint Data { get => stored; set { stored = value; } }
                public nuint Echo(nuint value) => value;
            }
            class Base {
                public nuint value;
                public Base(nuint data) { value = data; }
                public virtual nuint Echo(nuint data) => data;
            }
            class Derived : Base { public Derived() : base(0xffffffff) {} }
            int main() {
                nuint(nuint)* callback = Echo;
                if (Echo(0xffffffff) != 4294967295ul || callback(0xffffffff) != 4294967295ul) return 1;
                if (Default() != 4294967295ul) return 2;
                if (Maximum() != 18446744073709551615ul) return 7;
                Value value = new Value(0xffffffff);
                if (value.Data != 4294967295ul) return 3;
                value.Data = 0xffffffff;
                if (value.Data != 4294967295ul) return 4;
                IValue* view = &value;
                if (view.Echo(0xffffffff) != 4294967295ul) return 5;
                Derived derived = new Derived();
                if (derived.value != 4294967295ul || derived.Echo(0xffffffff) != 4294967295ul) return 6;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ConstantFunctionsAndRuntimeCallsAgreeForLiteralConversions()
    {
        var result = CompilerTestHelper.Run("""
            const nuint Echo(nuint value) => value;
            const nuint Default(nuint value = 0xffffffff) => value;
            const nuint Maximum(nuint value = 0xfffffffffffffffful) => value;
            const bool Check() {
                nuint value = 0xfffffffffffffffful;
                if (value != 18446744073709551615ul || Maximum() != value) return false;
                value = 0;
                value += 0xffffffff;
                if (value != 4294967295ul) return false;
                nint minimum = -9223372036854775808l;
                if (!(minimum < 0)) return false;
                return Echo(0xffffffff) == value && Default() == value;
            }
            const bool folded = Check();
            int main() { if (!folded || !Check()) return 1; return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("nuint value = -1;")]
    [InlineData("nint value = 18446744073709551615ul;")]
    [InlineData("int value = 4294967295ul;")]
    [InlineData("byte value = 256;")]
    [InlineData("void Accept(nuint value) {} void F() { Accept(-1); }")]
    [InlineData("void Accept(nint value) {} void F() { Accept(18446744073709551615ul); }")]
    public void LiteralConversionsStillRejectOutOfRangeValues(string source)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(source));
}
