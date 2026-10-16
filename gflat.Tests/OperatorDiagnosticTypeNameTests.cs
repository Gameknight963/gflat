using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class OperatorDiagnosticTypeNameTests
{
    [Theory]
    [InlineData("Box<int>", "struct Box<T>{}")]
    [InlineData("Family<int>::Item", "struct Family<T>{public struct Item{}}")]
    public void BuiltinOperandErrorsUseSourceTypeNames(string spelling,string declarations)
    {
        string source=$$$"""
            namespace Lib { {{{declarations}}} }
            int main(){Lib::{{{spelling}}} value=new Lib::{{{spelling}}}();return value+1;}
            """;
        string expected=$"Operator '+' cannot be applied to operands of type 'Lib::{spelling}' and 'int'";
        AssertMessage(source,expected);
    }

    [Fact]
    public void PrivateOperatorOwnerUsesTheGenericSourceName()
    {
        const string source="""
            namespace Lib {
                struct Box<T>{private static int operator +(Box<T> left,Box<T> right)=>0;}
            }
            int main(){Lib::Box<int> value=new Lib::Box<int>();return value+value;}
            """;
        AssertMessage(source,"Cannot access private operator '+' of 'Lib::Box<int>'");
    }

    private static void AssertMessage(string source,string expected)
    {
        var file=new SourceFile("operator-name.gf",source);
        var diagnostic=Assert.Single(Compiler.Analyze([file],new(){NoStdLib=true}));
        Assert.Equal(expected,diagnostic.Message);
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(file,options:new(){NoStdLib=true}));
        Assert.Equal(expected,error.Description);
        Assert.Equal(diagnostic.Span,error.Span);
    }
}
