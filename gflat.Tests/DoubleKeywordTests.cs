using gflat.diagnostics;

namespace gflat.Tests;

public sealed class DoubleKeywordTests
{
    [Fact]
    public void DoubleIsAReservedPrimitiveKeyword()
    {
        var token = Lexer.Tokenize("double")[0];
        // String assertion lets the RED test compile before TokenKind.Double exists.
        Assert.Equal("Double", token.Kind.ToString());
        Assert.Equal("double", token.Text);
        Assert.Equal(TokenKind.Identifier, Lexer.Tokenize("doubleValue")[0].Kind);
    }

    [Theory]
    [InlineData("int main(){int double=5;return 0;}")]
    [InlineData("int main(){int double=5;return (int)((double)-1);}")]
    [InlineData("alias double=int;int main()=>0;")]
    [InlineData("class double{}int main()=>0;")]
    [InlineData("struct double{}int main()=>0;")]
    [InlineData("int double()=>0;int main()=>0;")]
    [InlineData("void F(int double){}int main()=>0;")]
    [InlineData("class C{public int double;}int main()=>0;")]
    [InlineData("int F<double>()=>0;int main()=>0;")]
    public void DoubleCannotNameADeclaration(string source)
    {
        var diagnostics = new DiagnosticBag();
        Parser.Parse(Lexer.Tokenize(source), diagnostics);
        Assert.Contains(diagnostics.Items, diagnostic => diagnostic.Descriptor.Id == "GF0005" &&
            diagnostic.Message.Contains("Identifier", StringComparison.Ordinal) &&
            diagnostic.Message.Contains("Double", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("alias Real=double;Real F(Real value)=>value;int main()=>0;")]
    [InlineData("void F(double* value,readonly(double)* borrow,immutable(double)* frozen){}int main()=>0;")]
    [InlineData("double F(double value=1.25d)=>value;class C{public static double G(double value=2.5d)=>value;}int main()=>(int)(F()+C::G());")]
    [InlineData("double F(double(double)* callback,double value)=>callback(value);int main()=>0;")]
    [InlineData("int main(){double value=1.5d;return (int)((double)-1+value);}")]
    [InlineData("int main(){double value=new double();return (int)value;}")]
    [InlineData("int main(){double value=default(double);return (int)value;}")]
    [InlineData("const bool Layout=sizeof(double)==8&&alignof(double)==8;int main(){if(!Layout)return 1;return 0;}")]
    [InlineData("int main(){Standard::TypeInfo info=typeof(double);return (int)info.Size-8;}")]
    [InlineData("int main(){readonly(char)* name=nameof(double);return (int)name[0]-(int)'d';}")]
    [InlineData("int main(){int value=5;return (value)-1;}")]
    public void ExistingDoubleTypePathsRemainAvailable(string source)
        => CompilerTestHelper.Check(source);
}
