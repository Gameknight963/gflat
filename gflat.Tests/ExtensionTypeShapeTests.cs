namespace gflat.Tests;

public sealed class ExtensionTypeShapeTests
{
    [Theory]
    [InlineData("int(int)", false)]
    [InlineData("int(int)", true)]
    [InlineData("readonly(int(int))", false)]
    [InlineData("readonly(int(int))", true)]
    [InlineData("int(int)*", false)]
    [InlineData("int(int)*", true)]
    [InlineData("int(int)*?", false)]
    [InlineData("int(int)*?", true)]
    public void ExtensionCallsPreserveFunctionSignatureAndPointerTypeArguments(string type, bool direct)
    {
        string call = direct ? "Read<int>(handler, 0)" : "handler.Read(0)";
        Assert.Equal(42, CompilerTestHelper.Run($$"""
            struct Handler<F>{public int value;}
            int Read<T>(this Handler<{{type}}> self,T value)=>42;
            int main(){Handler<{{type}}> handler=new Handler<{{type}}>();return {{call}};}
            """).ExitCode);
    }

    [Theory]
    [InlineData("int(int)", 42)]
    [InlineData("int(int)*", 1)]
    public void InferredTypeArgumentsRetainTheirLayoutMeaning(string type, int expected)
        => Assert.Equal(expected, CompilerTestHelper.Run($$"""
            struct Handler<F>{public int value;}
            int HasValueLayout<F>(this Handler<F> self){if(typeof(F).HasLayout)return 1;return 42;}
            int main(){Handler<{{type}}> handler=new Handler<{{type}}>();return handler.HasValueLayout();}
            """).ExitCode);
}
