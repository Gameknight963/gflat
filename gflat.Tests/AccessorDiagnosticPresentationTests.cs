using gflat.diagnostics;

namespace gflat.Tests;

public sealed class AccessorDiagnosticPresentationTests
{
    [Theory]
    [InlineData("class", false)]
    [InlineData("struct", false)]
    [InlineData("class", true)]
    [InlineData("struct", true)]
    public void ReadonlyReceiverFailuresDescribeTheSourceAccessor(string kind, bool indexer)
    {
        string property = indexer ? "this[int index]" : "Value";
        string read = indexer ? "(*value)[0]" : "value.Value";
        string label = indexer ? "indexer accessor 'this[]'" : "property accessor 'Value'";
        string source = "namespace Lib{" + kind + " Entry<T>{public int " + property + "{get{return 0;}}}}" +
            "void F(readonly(Lib::Entry<int>)* value){" + read + ";}int main()=>0;";
        AssertError(source, $"Cannot call non-readonly {label} on readonly instance", read);
    }

    [Theory]
    [InlineData("class", "get")]
    [InlineData("struct", "get")]
    [InlineData("class", "set")]
    [InlineData("struct", "set")]
    public void MissingInterfaceAccessorsUseSourceNames(string kind, string accessor)
    {
        string declaration = $"{kind} Entry<T>:I{{}}";
        string source = $"namespace Lib{{interface I{{int Value{{{accessor};}}}}{declaration}}}int main(){{Lib::Entry<int> value=new Lib::Entry<int>();return 0;}}";
        AssertError(source, $"{char.ToUpperInvariant(kind[0]) + kind[1..]} 'Lib::Entry<int>' does not implement interface property accessor 'Lib::I.Value'", declaration);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void InterfaceGetterReturnMismatchUsesSourceNames(string kind)
    {
        string source = $"namespace Lib{{interface I{{int Value{{get;}}}}{kind} Entry<T>:I{{public long Value{{get{{return 0;}}}}}}}}int main(){{Lib::Entry<int> value=new Lib::Entry<int>();return 0;}}";
        AssertError(source, $"Property accessor 'Value' in {kind} 'Lib::Entry<int>' has return type 'long', but interface 'Lib::I' requires 'int'", "get{return 0;}");
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void InterfaceSetterParameterMismatchUsesSourceNames(string kind)
    {
        string source = $"namespace Lib{{interface I{{int Value{{set;}}}}{kind} Entry<T>:I{{public long Value{{set{{}}}}}}}}int main(){{Lib::Entry<int> value=new Lib::Entry<int>();return 0;}}";
        AssertError(source, $"Parameter 'value' of property accessor 'Value' in {kind} 'Lib::Entry<int>' has type 'long', but interface 'Lib::I' expects 'int'", "set");
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void OrdinaryReadonlyMethodDiagnosticsKeepTheirWording(string kind)
        => AssertError($"{kind} Entry{{public int Read()=>0;}}void F(readonly(Entry)* value){{value.Read();}}",
            "Cannot call non-readonly method 'Read' on readonly instance", "value.Read()");

    private static void AssertError(string source, string expected, string offending)
    {
        var errors = Compiler.Analyze([new SourceFile("accessor-presentation.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(errors, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(expected, error.Message);
        Assert.Equal("accessor-presentation.gf", error.FilePath);
        Assert.Equal(offending, source.Substring(error.Span.Start, error.Span.Length));
    }
}
