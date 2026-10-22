using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ParameterOwnerDelimiterRecoveryTests
{
    [Theory]
    [InlineData("public void Pending(int")]
    [InlineData("public void Pending(")]
    [InlineData("public C(int")]
    [InlineData("public static C operator +(C")]
    [InlineData("private extern int Pending(int")]
    public void IncompleteParameterListPreservesOwningClassAndGlobalMethods(string pending)
    {
        string text=$$"""
            class C {
                public int Read(){return 42;}
                {{pending}}
            }
            int Healthy(){C c=new();return c.Read();}
            """;
        Check(text);
    }

    [Theory]
    [InlineData("extern int Pending(int;")]
    [InlineData("int Pending(int;")]
    [InlineData("extern int Pending(;")]
    public void ParameterSemicolonPreservesLaterGlobalBinding(string pending)
        => Check(pending+"\nclass C{public int Read(){return 42;}}\nint Healthy(){C c=new();return c.Read();}");

    private static void Check(string text)
    {
        var source=new SourceFile(Path.GetFullPath("pending-parameters.gf"),text);
        string companion="int Companion(){return Healthy();}";
        var other=new SourceFile(Path.GetFullPath("healthy-companion.gf"),companion);
        var snapshot=new AnalysisSnapshot([source,other],new(){NoStdLib=true});
        Assert.Contains(snapshot.Diagnostics,d=>d.Severity==DiagnosticSeverity.Error);
        var methods=snapshot.Nodes.Select(n=>n.Node).OfType<CompilationUnit>().Single().Members.OfType<MethodDeclaration>().ToArray();
        Assert.Contains(methods,m=>m.Name=="Healthy");
        Assert.Contains(methods,m=>m.Name=="Companion");
        var companionCall=Assert.Single(snapshot.Nodes.Where(node=>node.Span.SourceId==other.Id)
            .Select(node=>node.Node).OfType<CallExpression>());
        Assert.NotNull(snapshot.Checker!.GetResolvedCall(companionCall));
        Assert.Equal("int",TypeChecker.TypeName(snapshot.Checker.GetType(companionCall)));
        var model=new EditorModel(snapshot,[source,other]);
        var read=model.Definition(source.Path,text.IndexOf("Read()",StringComparison.Ordinal));
        Assert.NotNull(read);
        Assert.Equal(read,model.Definition(source.Path,text.LastIndexOf("Read()",StringComparison.Ordinal)));
        var healthyDefinition=model.Definition(source.Path,text.IndexOf("Healthy()",StringComparison.Ordinal));
        Assert.NotNull(healthyDefinition);
        Assert.Equal(healthyDefinition,model.Definition(other.Path,companion.IndexOf("Healthy()",StringComparison.Ordinal)));
        Assert.DoesNotContain(snapshot.Diagnostics,d=>d.Message.Contains("Unknown function 'Healthy'",StringComparison.Ordinal));
        Assert.Throws<gflat.CompileExceptions.TypeCheckException>(()=>Compiler.Check([source,other],options:new(){NoStdLib=true}));
    }
}
