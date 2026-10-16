using gflat.diagnostics;

namespace gflat.Tests;

public sealed class SourceDiagnosticPresentationTests
{
    [Theory]
    [InlineData("namespace Lib{class Family<T>{public class Item{private int secret;}}}int main(){Lib::Family<int>::Item x=new Lib::Family<int>::Item();return x.secret;}", "Cannot access private field 'secret' of class 'Lib::Family<int>::Item'", "x.secret")]
    [InlineData("namespace Lib{class Family<T>{public class Item{private int Secret()=>1;}}}int main(){Lib::Family<int>::Item x=new Lib::Family<int>::Item();return x.Secret();}", "Cannot access private method 'Secret' of class 'Lib::Family<int>::Item'", "x.Secret")]
    [InlineData("namespace Lib{struct Box<T>{public T value;~Box(){}}}int main(){Lib::Box<int> a=new Lib::Box<int>();Lib::Box<int> b=a;return 0;}", "Cannot copy value of type 'Lib::Box<int>' because it has no accessible copy constructor. Borrow through a pointer or move a local.", "a")]
    [InlineData("namespace Lib{static class Box{}}void Use(Lib::Box item){}int main()=>0;", "Static class 'Lib::Box' cannot be used as an instance type", "Lib::Box")]
    [InlineData("namespace Lib{struct Box<T>{public T value;}}void Use(Lib::Box<long> value){}int main(){Lib::Box<int> x=new Lib::Box<int>();Use(x);return 0;}", "Argument 1 of 'Use': cannot pass 'Lib::Box<int>' as 'Lib::Box<long>'", "x")]
    [InlineData("namespace Lib{struct Box<T>{public T value;}struct Holder{public Box<int> value=new Box<long>();}}int main()=>0;", "Cannot assign expression of type 'Lib::Box<long>' to field 'value' of type 'Lib::Box<int>'", "new Box<long>()")]
    [InlineData("namespace Lib{interface I{int Read();}}void Use(Lib::I* item){item.Missing();}int main()=>0;", "Interface 'Lib::I' has no method 'Missing'", "item.Missing")]
    [InlineData("namespace Lib{interface I{void Read();}struct Box<T:I>{public int value;}}int main(){Lib::Box<int> x=new Lib::Box<int>();return 0;}", "Type 'int' does not satisfy constraint 'I' for type parameter 'T' on struct 'Lib::Box'", "Lib::Box<int>")]
    public void RawDiagnosticsUseSourceTypeNames(string source, string expected, string offending)
        => AssertError(source, expected, offending);

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void InterfaceReturnTypesUseSourceNames(string kind)
        => AssertError($$"""
            namespace Lib {struct Box<T>{public T value;}interface I{Box<int> Read();}
            {{kind}} Impl<T>:I{public Box<long> Read()=>new Box<long>();} }
            int main(){Lib::Impl<int> item=new Lib::Impl<int>();return 0;}
            """, $"Method 'Read' in {kind} 'Lib::Impl<int>' has return type 'Lib::Box<long>', but interface 'Lib::I' requires 'Lib::Box<int>'", "Read()");

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void InterfaceParameterTypesUseSourceNames(string kind)
        => AssertError($$"""
            namespace Lib {struct Box<T>{public T value;}interface I{void Read(Box<int>* value);}
            {{kind}} Impl<T>:I{public void Read(Box<long>* value){} } }
            int main(){Lib::Impl<int> item=new Lib::Impl<int>();return 0;}
            """, $"Parameter 'value' of method 'Read' in {kind} 'Lib::Impl<int>' has type 'Lib::Box<long>*', but interface 'Lib::I' expects 'Lib::Box<int>*'", "Box<long>* value");

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void MissingInterfaceMembersUseQualifiedContractNames(string kind)
        => AssertError($"namespace Lib{{interface I{{int Read();}}{kind} Impl<T>:I{{}}}}int main(){{Lib::Impl<int> item=new Lib::Impl<int>();return 0;}}",
            $"{char.ToUpperInvariant(kind[0]) + kind[1..]} 'Lib::Impl<int>' does not implement interface method 'Lib::I.Read'", "Impl");

    [Theory]
    [InlineData("class", "int Read() nothrow;", "public int Read()=>0;", "must match nothrow specification of interface method 'Lib::I.Read'")]
    [InlineData("struct", "int Read() nothrow;", "public int Read()=>0;", "must match nothrow specification of interface method 'Lib::I.Read'")]
    [InlineData("class", "readonly int Read();", "public int Read()=>0;", "must be marked readonly to implement interface method 'Lib::I.Read'")]
    [InlineData("struct", "readonly int Read();", "public int Read()=>0;", "must be marked readonly to implement interface method 'Lib::I.Read'")]
    [InlineData("class", "int Read(int value);", "public int Read()=>0;", "has 0 parameters, but interface 'Lib::I' expects 1")]
    [InlineData("struct", "int Read(int value);", "public int Read()=>0;", "has 0 parameters, but interface 'Lib::I' expects 1")]
    public void InterfaceModifierAndArityDiagnosticsKeepSourceOwners(string kind, string contract, string implementation, string detail)
        => AssertError($"namespace Lib{{interface I{{{contract}}}{kind} Impl<T>:I{{{implementation}}}}}int main(){{Lib::Impl<int> item=new Lib::Impl<int>();return 0;}}",
            $"Method 'Read' in {kind} 'Lib::Impl<int>' {detail}", implementation);

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void InterfaceParameterErrorsPointIntoTheImplementationsSourceFile(string kind)
    {
        const string contracts = "namespace Contract{public struct Box<T>{public T value;}public interface I{void Read(Box<int>* value);}}";
        string implementation = $"namespace App{{{kind} Impl<T>:Contract::I{{public void Read(readonly(Contract::Box<long>)* value){{}}}}}}";
        var diagnostics = Compiler.Analyze([new SourceFile("contract.gf", contracts), new SourceFile("implementation.gf", implementation),
            new SourceFile("caller.gf", "int main(){App::Impl<int> item=new App::Impl<int>();return 0;}")], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal($"Parameter 'value' of method 'Read' in {kind} 'App::Impl<int>' has type 'readonly(Contract::Box<long>)*', but interface 'Contract::I' expects 'Contract::Box<int>*'", error.Message);
        Assert.Equal("implementation.gf", error.FilePath);
        Assert.Equal("readonly(Contract::Box<long>)* value", implementation.Substring(error.Span.Start, error.Span.Length));
    }

    private static void AssertError(string source, string expected, string offending)
    {
        var diagnostics = Compiler.Analyze([new SourceFile("presentation.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(expected, error.Message);
        Assert.DoesNotContain("$", error.Message);
        Assert.Equal("presentation.gf", error.FilePath);
        string spanText = source.Substring(error.Span.Start, error.Span.Length);
        if (offending is "x" or "a" || offending.StartsWith("new ", StringComparison.Ordinal)) Assert.Equal(offending, spanText);
        else Assert.Contains(offending, spanText);
    }
}
