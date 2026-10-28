using gflat.diagnostics;

namespace gflat.Tests;

public sealed class SymbolCollectionDiagnosticPresentationTests
{
    [Theory]
    [InlineData("namespace Lib{struct Resource{public ~Resource(){} public ~Resource(){}}}int main()=>0;", "Struct 'Lib::Resource' already defines a destructor")]
    [InlineData("namespace Lib{struct Resource{virtual ~Resource(){}}}int main()=>0;", "Struct 'Lib::Resource' destructor cannot be virtual")]
    [InlineData("namespace Lib{class Resource{public ~Resource(){} public ~Resource(){}}}int main()=>0;", "Class 'Lib::Resource' already defines a destructor")]
    [InlineData("namespace Lib{class Outer{public struct Resource{public ~Resource(){} public ~Resource(){}}}}int main()=>0;", "Struct 'Lib::Outer::Resource' already defines a destructor")]
    [InlineData("namespace Lib{interface I{int Read(); int Read();}}int main()=>0;", "Interface 'Lib::I' already contains a method named 'Read'")]
    [InlineData("namespace Lib{enum E{A} enum F{B=E.A}}int main()=>0;", "Enum member 'Lib::E::A' must be accessed with '::', not '.'")]
    [InlineData("namespace Lib{enum E{A}}int main()=>Lib::E.A;", "Enum member 'Lib::E::A' must be accessed with '::', not '.'")]
    [InlineData("namespace Lib{struct Resource<T>{virtual ~Resource(){}}}int main(){Lib::Resource<int> value=new Lib::Resource<int>();return 0;}", "Struct 'Lib::Resource<int>' destructor cannot be virtual")]
    [InlineData("namespace Lib{class Outer<T>{public struct Resource{public ~Resource(){} public ~Resource(){}}}}int main(){Lib::Outer<int>::Resource value=new Lib::Outer<int>::Resource();return 0;}", "Struct 'Lib::Outer<int>::Resource' already defines a destructor")]
    [InlineData("namespace Lib{struct Resource{public ~Other(){}}}int main()=>0;", "Destructor name '~Other' does not match struct name 'Lib::Resource'")]
    [InlineData("namespace Lib{class Resource{public ~Other(){}}}int main()=>0;", "Destructor name '~Other' does not match class name 'Lib::Resource'")]
    [InlineData("namespace Lib{interface I{int value;}}int main()=>0;", "Interface 'Lib::I' can only contain method declarations")]
    [InlineData("namespace Lib{class Resource<T>{public ~Resource(){} public ~Resource(){}}}int main(){Lib::Resource<int> value=new Lib::Resource<int>();return 0;}", "Class 'Lib::Resource<int>' already defines a destructor")]
    public void CollectionAndEnumInitializerErrorsUseSourceNames(string source, string expected)
    {
        var diagnostics = Compiler.Analyze([new SourceFile("collection-presentation.gf", source)], new() { NoStdLib = true });
        var errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, error => error.Message == expected);
        Assert.All(errors, error => Assert.DoesNotContain('$', error.Message));
    }
}
