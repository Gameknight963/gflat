using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class FunctionGroupReinterpretTests
{
    [Theory]
    [InlineData("void* address = F.reinterpret<void*>();")]
    [InlineData("void(int)* callback = F.reinterpret<void(int)*>();")]
    [InlineData("void* address = (&F).reinterpret<void*>();")]
    public void ReinterpretTargetDoesNotSelectAnUnresolvedOverload(string expression)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "void F(int value){} void F(long value){} void Probe(){" + expression + "}",
            options: new() { NoStdLib = true, NoBuiltins = true }));
        Assert.Contains("target function pointer signature", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OrdinaryTargetSelectionPrecedesExplicitRawAddressConversion(int optimization)
    {
        const string program = """
            int F(int value) nothrow => value + 1;
            long F(long value) nothrow => value + 100l;
            int main() nothrow {
                void* selected = ((int(int)*)F).reinterpret<void*>();
                int(int)* first = selected.reinterpret<int(int)*>();
                if (first(41) != 42) return 1;
                int(int)* variable = F;
                void* address = variable.reinterpret<void*>();
                int(int)* second = address.reinterpret<int(int)*>();
                if (second(41) != 42 || first != variable || second != variable) return 2;
                return 0;
            }
            """;
        string[] names = ["core/Exception.gf", "libc/Memory/allocation.gf", "libc/Exceptions/unhandled.gf"];
        var sources = names.Select(name => new SourceFile(name,
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("function-group-reinterpret.gf", program));
        string ir = Compiler.Emit(sources, options: new()
        {
            NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true
        });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
