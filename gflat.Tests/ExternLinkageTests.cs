using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ExternLinkageTests
{
    [Fact]
    public void BareExternUsesTheLanguageDefinitionSymbolForCallsAndAddresses()
    {
        const string declaration = "namespace Example { extern int Answer(int value); } int main() { int(int)* answer = Example::Answer; return answer(42) + Example::Answer(0); }";
        string ir = Compiler.Emit(declaration, options: new() { NoStdLib = true, NoBuiltins = true });
        string definition = Compiler.Emit("namespace Example { int Answer(int value) => value; }", options: new() { NoStdLib = true, NoBuiltins = true });
        Assert.Contains("declare i32 @gflat$Example$Answer(i32)", ir);
        Assert.Contains("@gflat$Example$Answer", definition);
        Assert.DoesNotContain("@Answer(", ir);
    }

    [Fact]
    public void ExplicitCLinkageUsesTheNativeSymbol()
    {
        string ir = Compiler.Emit("extern \"C\" int abs(int value); int main() => abs(-42);",
            options: new() { NoStdLib = true, NoBuiltins = true });
        Assert.Contains("declare i32 @abs(i32)", ir);
        Assert.Equal(42, CompilerTestHelper.RunIr(ir).ExitCode);
    }

    [Fact]
    public void VariadicDeclarationsRequireCLinkage()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("extern int print(readonly(char)* format, ...);",
            options: new() { NoStdLib = true, NoBuiltins = true }));
}
