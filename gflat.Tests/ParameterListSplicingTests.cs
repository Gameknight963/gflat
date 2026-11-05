using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ParameterListSplicingTests
{
    [Theory]
    [InlineData("int Add(int left, int right) => left + right; int Multiply([:reflect(Add).Parameters:]) => left * right; int main() => Multiply(6, 7);")]
    [InlineData("int Add(int left, int right) => left + right; int Forward([:reflect(Add).Parameters:]) => Add([:reflect(Add).Parameters:]); int main() => Forward(20, 22);")]
    [InlineData("int Add(int left, int right) => left + right; int Forward([:reflect(Add).Parameters:]) => Add([:reflect(Forward).Parameters:]); int main() => Forward(20, 22);")]
    [InlineData("int Target() => 42; int Forward([:reflect(Target).Parameters:]) => Target([:reflect(Target).Parameters:]); int main() => Forward();")]
    [InlineData("int Add(int value = 42) => value; int Forward([:reflect(Add).Parameters:]) => Add([:reflect(Add).Parameters:]); int main() => Forward();")]
    [InlineData("void Set(ref int value) { value = 42; } void Forward([:reflect(Set).Parameters:]) { Set([:reflect(Set).Parameters:]); } int main() { int value = 0; Forward(ref value); return value; }")]
    [InlineData("int Target(int value) => value; int Forward(int prefix, [:reflect(Target).Parameters:], int suffix) => prefix + value + suffix; int main() => Forward(10, 20, 12);")]
    [InlineData("int Forward([:reflect(Target).Parameters:]) => Target([:reflect(Target).Parameters:]); int Target(int value) => value; int main() => Forward(42);")]
    [InlineData("int Target(int value) => value; struct Box { public int Value; public Box([:reflect(Target).Parameters:]) { Value = value; } } int Forward([:reflect(Target).Parameters:]) { Box box = new Box([:reflect(Target).Parameters:]); return box.Value; } int main() => Forward(42);")]
    [InlineData("int Target(int value) => value; int main() { int(int)* function = ([:reflect(Target).Parameters:]) => value; return function(42); }")]
    [InlineData("int Target(int value) => value; int main() { int(int)* function = ([:reflect(Target).Parameters:]) => Target([:reflect(Target).Parameters:]); return function(42); }")]
    [InlineData("int Target(int value) => value; int Forward([:reflect(Target).Parameters:]) => value; int Forward(long value) => 0; int main() => Forward(42);")]
    [InlineData("int Target(int prefix, int value, int suffix) => prefix + value + suffix; int Source(int value) => value; int Forward([:reflect(Source).Parameters:]) => Target(10, [:reflect(Source).Parameters:], 12); int main() => Forward(20);")]
    [InlineData("int Target(int value) => value; extern int abs([:reflect(Target).Parameters:]); int main() => abs(-42);")]
    [InlineData("int Target(int value) => value; int Forward<T>([:reflect(Target).Parameters:]) => Target([:reflect(Target).Parameters:]); int main() => Forward<long>(42);")]
    [InlineData("int Target(int value) => value; const reflection signature = reflect(Target); [:signature.ReturnType:] Forward([:signature.Parameters:]) => Target([:signature.Parameters:]); int main() => Forward(42);")]
    [InlineData("struct Owner { public int Value; public Owner(int value) { Value = value; } private copy Owner(immutable(Owner)* source) { Value = source.Value; } } int Target(Owner owner) => owner.Value; int Forward([:reflect(Target).Parameters:]) => Target(move [:reflect(Target).Parameters:]); int main() { Owner owner = new(42); return Forward(move owner); }")]
    public void GeneratedSignaturesAndForwardingExecute(string source)
    {
        string ir = Compiler.Emit(source, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        Assert.Equal(42, CompilerTestHelper.RunIr(ir).ExitCode);
    }

    [Fact]
    public void DefaultValuesRetainTheirDeclarationNamespace()
    {
        string ir = Compiler.Emit("""
            namespace Original { const int Default = 42; int Target(int value = Default) => value; }
            namespace Other { const int Default = 7; int Forward([:reflect(Original::Target).Parameters:]) => value; }
            int main() => Other::Forward();
            """, options: new() { NoStdLib = true, NoBuiltins = true });
        Assert.Equal(42, CompilerTestHelper.RunIr(ir).ExitCode);
    }

    [Theory]
    [InlineData("int Target(int value) => value; int Broken([:reflect(int):]) => 0;")]
    [InlineData("int Target([:reflect(Target).Parameters:]) => 0;")]
    [InlineData("int Target(int value) => value; int Broken(int value, [:reflect(Target).Parameters:]) => value;")]
    [InlineData("int Target(int value) => value; int Broken() => Target([:reflect(Target).Parameters:]);")]
    public void InvalidSignatureSplicesProduceDiagnostics(string source)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true, NoBuiltins = true }));
}
