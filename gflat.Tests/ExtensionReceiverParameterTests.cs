using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ExtensionReceiverParameterTests
{
    [Theory]
    [InlineData("extension Box<T> { public T Read() => (*this).value; }", "Box<int> box = new(); box.value = 42; return box.Read();")]
    [InlineData("extension Pair<T, T> { public int Read() => 42; }", "Pair<int, int> pair = new(); return pair.Read();")]
    [InlineData("extension Box<global::Entry> { public int Read() => 42; }", "Box<Entry> box = new(); return box.Read();")]
    [InlineData("extension Box<T : IValue> { public int Read() => (*this).value.Read(); }", "Box<Entry> box = new(); return box.Read();")]
    public void ReceiverPatternDeclaresParametersAndPreservesConcreteTypes(string extension, string body)
    {
        string ir = Compiler.Emit("struct T {} interface IValue { int Read(); } struct Entry : IValue { public int Read() => 42; } struct Box<T> { public T value; } struct Pair<A, B> {} " + extension + " int main() { " + body + " }",
            options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        Assert.Equal(42, CompilerTestHelper.RunIr(ir).ExitCode);
    }

    [Fact]
    public void QualifiedReceiverArgumentLimitsApplicability()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "struct Entry {} struct Box<T> {} extension Box<global::Entry> { public int Read() => 42; } int main() { Box<int> box = new(); return box.Read(); }",
            options: new() { NoStdLib = true, NoBuiltins = true }));

    [Fact]
    public void ConcreteArgumentsCannotDeclareConstraints()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "interface IValue {} struct Box<T> {} extension Box<int : IValue> { public int Read() => 42; }",
            options: new() { NoStdLib = true, NoBuiltins = true }));
}
