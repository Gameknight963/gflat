using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class PropertyAndDefaultDiagnosticTypeNameTests
{
    [Fact]
    public void PropertyAssignmentShowsBothGenericTypes()
    {
        const string source="""
            namespace Lib {struct Box<T>{}struct Holder{public Box<int> Value{get;set;}}}
            int main(){Lib::Holder holder=new Lib::Holder();Lib::Box<long> value=new Lib::Box<long>();holder.Value=value;return 0;}
            """;
        AssertMessage(source,"Cannot assign 'Lib::Box<long>' to property 'Value' of type 'Lib::Box<int>'");
    }

    [Fact]
    public void IndexerArgumentShowsBothGenericTypes()
    {
        const string source="""
            namespace Lib {struct Box<T>{}struct Holder{public int this[Box<int> index]{set{}}}}
            int main(){Lib::Holder holder=new Lib::Holder();Lib::Box<long> value=new Lib::Box<long>();holder[value]=1;return 0;}
            """;
        AssertMessage(source,"Cannot pass 'Lib::Box<long>' as indexer argument 'Lib::Box<int>'");
    }

    [Theory]
    [InlineData("Lib::Box<int>*")]
    [InlineData("readonly(Lib::Box<int>)*?")]
    [InlineData("Lib::Family<int>::Item*")]
    public void OptionalDefaultConversionUsesTheGenericPointerName(string type)
    {
        string source=$$$$"""
            namespace Lib {struct Box<T>{}struct Family<T>{public struct Item{}}}
            void F({{{{type}}}} value=1){}
            """;
        AssertMessage(source,$"Default for parameter 'value' cannot convert to '{type}'");
    }

    [Fact]
    public void OptionalDefaultInitializationUsesTheGenericName()
    {
        const string source="""
            namespace Lib {struct Box<T>{public Box(int value){}}}
            void F(Lib::Box<int> value=default){}
            """;
        AssertMessage(source,"Type 'Lib::Box<int>' cannot be default initialized");
    }

    [Fact]
    public void MissingNameofMemberUsesTheNestedGenericOwnerName()
    {
        const string source="""
            namespace Lib {struct Family<T>{public struct Item{}}}
            int main(){Lib::Family<int>::Item value=new Lib::Family<int>::Item();nameof(value.Missing);return 0;}
            """;
        AssertMessage(source,"Type 'Lib::Family<int>::Item' has no member 'Missing'");
    }

    private static void AssertMessage(string source,string expected)
    {
        var file=new SourceFile("property-default-name.gf",source);
        var diagnostic=Assert.Single(Compiler.Analyze([file],new(){NoStdLib=true}));
        Assert.Equal(expected,diagnostic.Message);
        var error=Assert.Throws<TypeCheckException>(()=>Compiler.Check(file,options:new(){NoStdLib=true}));
        Assert.Equal(expected,error.Description);
        Assert.Equal(diagnostic.Span,error.Span);
    }
}
