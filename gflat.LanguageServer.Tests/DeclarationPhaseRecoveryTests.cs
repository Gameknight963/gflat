using gflat;
using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class DeclarationPhaseRecoveryTests
{
    [Theory]
    [InlineData("Missing<int> Broken()=>default;")]
    [InlineData("void Broken(Missing<int> value){}")]
    [InlineData("struct Broken{public Missing<int> value;}")]
    [InlineData("class Broken{public Missing<int> value;}")]
    [InlineData("void Broken(Missing<int> value){}void Broken(int value){}")]
    [InlineData("struct Broken{public Broken(Missing<int> value){}public Broken(int value){}}")]
    [InlineData("struct Broken{public Missing value;}")]
    public void InvalidDeclarationTypesDoNotAbortUnrelatedBodyAnalysis(string declaration)
    {
        var broken=new SourceFile("broken-declaration.gf","namespace Invalid{"+declaration+"}");
        AssertHealthy(broken);
    }

    [Fact]
    public void InvalidGenericBaseDoesNotDiscardItsValidSiblingInterface()
    {
        var broken=new SourceFile("broken-base.gf","interface Good{int Read();}interface Bad<T>{}struct Broken:Good,Bad<int,int>{public int Read()=>42;}");
        var snapshot=AssertHealthy(broken);
        var interfaces=snapshot.Checker!.GetStruct("Broken")!.Interfaces;
        Assert.Contains("Good",interfaces);
        Assert.DoesNotContain(interfaces,name=>name.StartsWith("Bad",StringComparison.Ordinal));
    }

    private static AnalysisSnapshot AssertHealthy(SourceFile broken)
    {
        var healthy=new SourceFile("healthy-declaration.gf","namespace Audit{public class Item{public int Read(int argument){int local=argument;return local;}}}int Healthy(){Audit::Item item=new Audit::Item();return item.Read(42);}");
        SourceFile[] sources=[broken,healthy];
        Assert.Throws<TypeCheckException>(()=>Compiler.Check(sources,options:new(){NoStdLib=true}));
        var snapshot=new AnalysisSnapshot(sources,new(){NoStdLib=true});
        Assert.Contains(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error&&error.FilePath==broken.Path);
        Assert.DoesNotContain(snapshot.Diagnostics,error=>error.Severity==DiagnosticSeverity.Error&&error.FilePath==healthy.Path);
        var local=Assert.Single(snapshot.Nodes.Where(n=>n.Span.SourceId==healthy.Id).Select(n=>n.Node).OfType<VariableDeclaration>(),n=>n.Name=="local");
        Assert.Equal("int",TypeChecker.TypeName(snapshot.Checker!.GetType(local)));
        var call=Assert.Single(snapshot.Nodes.Where(n=>n.Span.SourceId==healthy.Id).Select(n=>n.Node).OfType<CallExpression>());
        Assert.Equal("int",TypeChecker.TypeName(snapshot.Checker.GetType(call)));
        Assert.NotNull(snapshot.Checker.GetResolvedCall(call));
        return snapshot;
    }
}
