using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class LambdaParameterContractTests
{
    private const string Message = "Lambda parameters cannot be const; function pointers cannot express constant-argument requirements";

    [Theory]
    [InlineData("static(const int value)=>value")]
    [InlineData("(const int value)=>value")]
    [InlineData("static(const int value)=>{return value;}")]
    public void LambdaCannotDiscardAConstParameterRequirement(string lambda)
    {
        string text = "int Invoke(int runtime){int(int)* callback=" + lambda + ";return callback(runtime);}int main()=>Invoke(42);";
        var source = new SourceFile("lambda-const-parameter.gf", text);
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        Assert.Equal(Message, error.Description);
        Assert.Equal("const int value", text.Substring(error.Span.Start, error.Span.Length));
        var diagnostic = Assert.Single(Compiler.Analyze([source], new() { NoStdLib = true }));
        Assert.Equal(Message, diagnostic.Message);
        Assert.Equal(error.Span, diagnostic.Span);
    }

    [Fact]
    public void UnusedLambdaStillChecksItsParameterContract()
        => Assert.Contains(Message, Assert.Throws<TypeCheckException>(() => Compiler.Check(
            "int main(){int(int)* unused=static(const int value)=>value;return 0;}", options: new() { NoStdLib = true })).Message);

    [Fact]
    public void OrdinaryLambdaArgumentsAndNamedConstantParametersRemainSupported()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            int Constant(const int value)=>value;
            int Invoke(int runtime){int(int)* callback=static(int value)=>value;return callback(runtime);}
            int main()=>Invoke(Constant(42));
            """).ExitCode);

    [Fact]
    public void ReferenceLambdaParametersRetainTheirStorageContract()
        => Assert.Equal(42, CompilerTestHelper.Run("""
            int main(){int value=0;void(ref int)* callback=(ref int item)=>{item=42;};callback(ref value);return value;}
            """).ExitCode);
}
