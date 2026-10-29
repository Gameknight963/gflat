using gflat.diagnostics;

namespace gflat.Tests;

public sealed class FunctionGroupDiagnosticTests
{
    private const string Group = "void F(int value){} void F(long value){}\n";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AssignmentDiagnosticsKeepTheirCodeTargetAndExpressionSpan(bool assignment, bool address)
    {
        string expression = address ? "&F" : "F";
        string source = Group + (assignment
            ? "bool Zero()=>false;int main(){bool()* callback=&Zero;callback=" + expression + ";return 0;}"
            : "int main(){bool()* callback=" + expression + ";return 0;}");
        var diagnostics = Analyze(source);
        var mismatch = Assert.Single(diagnostics, d => d.Descriptor.Id == "GF1001");
        Assert.Equal("Cannot assign 'function group' to 'bool()*'", mismatch.Message);
        Assert.Equal(expression, source.Substring(mismatch.Span.Start, mismatch.Span.Length));
    }

    [Theory]
    [InlineData("class-field", "class Owner{public bool()* Callback=F;}")]
    [InlineData("struct-field", "struct Owner{public bool()* Callback=F;}")]
    [InlineData("global-field", "bool()* callback=F;")]
    [InlineData("return", "bool()* Get()=>F;")]
    [InlineData("method-argument", "void Accept(bool()* callback){} int main(){Accept(F);return 0;}")]
    [InlineData("extern-argument", "extern void Accept(bool()* callback);int main(){Accept(F);return 0;}")]
    [InlineData("indirect-argument", "void Accept(bool()* callback){} int main(){void(bool()*)* accept=&Accept;accept(F);return 0;}")]
    [InlineData("interface-argument", "interface I{void Accept(bool()* callback);}void Pass(I* receiver){receiver.Accept(F);}")]
    [InlineData("class-constructor", "class Owner{public Owner(bool()* callback){}}int main(){Owner value=new Owner(F);return 0;}")]
    [InlineData("struct-constructor", "struct Owner{public Owner(bool()* callback){}}int main(){Owner value=new Owner(F);return 0;}")]
    [InlineData("base-constructor", "class Base{public Base(bool()* callback){}}class Derived:Base{public Derived():base(F){}}")]
    [InlineData("property", "class Owner{public bool()* Callback{set{}}}void Pass(Owner* owner){owner.Callback=F;}")]
    [InlineData("indexer-argument", "struct Owner{public int this[bool()* callback]{get=>0;set{}}}void Pass(Owner* owner){(*owner)[F]=1;}")]
    public void OrdinaryConversionErrorsDescribeTheUnresolvedGroup(string context, string body)
    {
        _ = context;
        var diagnostics = Analyze(Group + body);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("function group", StringComparison.Ordinal));
        Assert.All(diagnostics, diagnostic => Assert.DoesNotContain("void()*", diagnostic.Message));
    }

    [Fact]
    public void ARealZeroArgumentVoidPointerKeepsItsActualSignature()
    {
        var diagnostic = Assert.Single(Analyze("void Zero(){}int main(){bool()* callback=&Zero;return 0;}"),
            d => d.Descriptor.Id == "GF1001");
        Assert.Equal("Cannot assign 'void()*' to 'bool()*'", diagnostic.Message);
        Assert.DoesNotContain("function group", diagnostic.Message);
    }

    [Fact]
    public void AnExplicitlySelectedOverloadKeepsItsActualSignature()
    {
        var diagnostic = Assert.Single(Analyze(Group + "int main(){bool()* callback=(void(int)*)F;return 0;}"),
            d => d.Descriptor.Id == "GF1001");
        Assert.Equal("Cannot assign 'void(int)*' to 'bool()*'", diagnostic.Message);
        Assert.DoesNotContain("function group", diagnostic.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TypedArrayElementsDescribeUnresolvedGroupsAndPreserveTheirSpans(bool nested, bool address)
    {
        string expression = address ? "&F" : "F";
        string arrayType = nested ? "bool()*[1][1]" : "bool()*[1]";
        string initializer = nested ? "[[" + expression + "]]" : "[" + expression + "]";
        string source = Group + "int main(){" + arrayType + " callbacks=" + initializer + ";return 0;}";
        var diagnostic = Assert.Single(Analyze(source), d =>
            d.Message.StartsWith("Cannot initialize array element", StringComparison.Ordinal));
        Assert.Equal("GF1000", diagnostic.Descriptor.Id);
        Assert.Equal("Cannot initialize array element of type 'bool()*' with 'function group'", diagnostic.Message);
        Assert.Equal(expression, source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedArrayElementsKeepSingletonAndExplicitlySelectedSignatures(bool selected)
    {
        string expression = selected ? "(void(int)*)F" : "&Zero";
        string definitions = selected ? Group : "void Zero(){}";
        string actual = selected ? "void(int)*" : "void()*";
        string source = definitions + "int main(){bool()*[1] callbacks=[" + expression + "];return 0;}";
        var diagnostic = Assert.Single(Analyze(source), d =>
            d.Message.StartsWith("Cannot initialize array element", StringComparison.Ordinal));
        Assert.Equal("GF1000", diagnostic.Descriptor.Id);
        Assert.Equal("Cannot initialize array element of type 'bool()*' with '" + actual + "'", diagnostic.Message);
        Assert.Equal(expression, source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        Assert.DoesNotContain("function group", diagnostic.Message);
    }

    [Fact]
    public void TypedArraysStillSelectOverloadsForEveryElementAndNestedRow()
        => Compiler.Check(Group + """
            int main(){
                void(int)*[2] callbacks=[F,&F];
                void(int)*[1][1] nested=[[F]];
                callbacks[0](1);callbacks[1](2);nested[0][0](3);
                return 0;
            }
            """, options: new() { NoStdLib = true });

    private static Diagnostic[] Analyze(string source)
        => Compiler.Analyze([new SourceFile("function-group-diagnostics.gf", source)], new() { NoStdLib = true })
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
}
