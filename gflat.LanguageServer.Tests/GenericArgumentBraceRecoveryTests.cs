using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.LanguageServer.Tests;

public sealed class GenericArgumentBraceRecoveryTests
{
    [Theory]
    [InlineData("interface I<T>{}class Broken:I<int {public int Read()=>42;}")]
    [InlineData("class Outer{public interface I<T>{}}class Broken:Outer::I<int {public int Read()=>42;}")]
    public void MissingGenericCloserDoesNotConsumeTheDeclarationBody(string declaration)
    {
        var source=new SourceFile("generic-brace.gf",declaration+"int Healthy(){Broken value=new();return value.Read();}");
        Assert.Throws<TypeCheckException>(()=>Compiler.Check(source,options:new(){NoStdLib=true}));
        var snapshot=new AnalysisSnapshot([source],new(){NoStdLib=true});
        Assert.Contains(snapshot.Diagnostics,error=>error.Descriptor.Id=="GF0005"&&error.Message.Contains("Greater"));
        var broken=Assert.Single(snapshot.Nodes.Select(n=>n.Node).OfType<ClassDeclaration>(),n=>n.SourceName=="Broken");
        Assert.Contains(broken.Members,n=>n is MethodDeclaration{Name:"Read"});
        var call=Assert.Single(snapshot.Nodes.Select(n=>n.Node).OfType<CallExpression>());
        Assert.Equal("int",TypeChecker.TypeName(snapshot.Checker!.GetType(call)));
        Assert.NotNull(snapshot.Checker.GetResolvedCall(call));
    }
}
