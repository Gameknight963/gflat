using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class FunctionSignatureTests
{
    [Fact]
    public void SignatureAliasesAndGenericArgumentsDescribeCallbacks()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            alias Transform = int(int);
            struct Callback<F> { public F* target; public Callback(F* callback) { target = callback; } }
            int Double(int value) => value * 2;
            int Apply(Transform* transform) => transform(21);
            int main() {
                Callback<int(int)> callback = new Callback<int(int)>(Double);
                return Apply(callback.target);
            }
            """).ExitCode);
    }

    [Theory]
    [InlineData("Double")]
    [InlineData("&Double")]
    [InlineData("Functions::Double")]
    [InlineData("&Functions::Double")]
    public void TargetSignatureSelectsOverloadedFunction(string expression)
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            int Double(int value) => value * 2;
            long Double(long value) => value * 3L;
            static class Functions {
                public static int Double(int value) => value * 2;
                public static long Double(long value) => value * 3L;
            }
            int main() { int(int)* callback = EXPRESSION; return callback(21); }
            """.Replace("EXPRESSION", expression)).ExitCode);
    }

    [Fact]
    public void FunctionNamesWorkAsArgumentsAndReturnValues()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            int Double(int value) => value * 2;
            int(int)* Factory() => Double;
            int Apply(int(int)* callback, int value) => callback(value);
            int main() => Apply(Factory(), 21);
            """).ExitCode);
    }

    [Theory]
    [InlineData("int(int) value;")]
    [InlineData("struct Bad { public int(int) field; }")]
    [InlineData("void Bad(int(int) parameter) {}")]
    public void SignatureHasNoValueStorage(string source)
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Emit(source));
    }

    [Theory]
    [InlineData("Double;")]
    [InlineData("&Double;")]
    public void OverloadGroupRequiresAnExpectedSignature(string statement)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit("""
            int Double(int value) => value * 2;
            long Double(long value) => value * 2L;
            void Test() { STATEMENT }
            """.Replace("STATEMENT", statement)));
        Assert.Contains("target function pointer signature", error.Message);
    }

    [Fact]
    public void DifferentSignaturesProduceDifferentGenericSpecializations()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            struct Callback<F> { public F* target; public Callback(F* callback) { target = callback; } }
            int Identity(int value) => value;
            int Answer() => 21;
            int main() {
                Callback<int(int)> first = new Callback<int(int)>(Identity);
                Callback<int()> second = new Callback<int()>(Answer);
                return first.target(21) + second.target();
            }
            """).ExitCode);
    }

    [Fact]
    public void NullableFunctionPointerAcceptsAFunctionName()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            int Answer() => 42;
            int main() { int()*? callback = Answer; return callback!(); }
            """).ExitCode);
    }
}
