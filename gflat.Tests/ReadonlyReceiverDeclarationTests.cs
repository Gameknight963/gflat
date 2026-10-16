using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.Tests;

public sealed class ReadonlyReceiverDeclarationTests
{
    private const string Message = "Readonly methods require an instance receiver";

    [Theory]
    [InlineData("public readonly int Read()=>42;")]
    [InlineData("namespace Lib{public readonly int Read()=>42;}")]
    [InlineData("class Entry{public static readonly int Read()=>42;}")]
    [InlineData("struct Entry{public static readonly int Read()=>42;}")]
    [InlineData("static class Entry{public static readonly int Read()=>42;}")]
    [InlineData("static interface I{readonly int Read();}")]
    [InlineData("class Box<T>{public static readonly int Read()=>42;}")]
    [InlineData("public readonly int Read<T>()=>42;")]
    [InlineData("class Entry{public static readonly int Value{get{return 42;}}}")]
    [InlineData("class Entry{public static int Value{readonly get{return 42;}}}")]
    [InlineData("struct Entry{public static int Value{readonly get{return 42;}}}")]
    [InlineData("static class Entry{public static int Value{readonly get{return 42;}}}")]
    [InlineData("static interface I{readonly int Value{get;}}")]
    [InlineData("namespace Lib{}public weak readonly int Lib::Read()=>42;")]
    public void ReceiverlessReadonlyDeclarationsAreRejectedOnce(string source)
    {
        var error = Assert.Single(Errors(source));
        Assert.Equal(Message, error.Message);
        Assert.Equal("readonly-receiver.gf", error.FilePath);
        Assert.True(error.Span.Length > 0);
        var strict = Assert.Throws<TypeCheckException>(() => Compiler.Check(new SourceFile("readonly-receiver.gf", source), options: new() { NoStdLib = true }));
        Assert.Equal(error.Message, strict.Description);
        Assert.Equal(error.Span.Start, strict.Span.Start);
        Assert.Equal(error.Span.Length, strict.Span.Length);
    }

    [Fact]
    public void InvalidReceiverMetadataCannotReachEmission()
        => Assert.Equal(Message, Assert.Throws<TypeCheckException>(() => Compiler.Emit("public readonly int Read()=>42;", options: new() { NoStdLib = true })).Description);

    [Theory]
    [InlineData("class Entry{public readonly int Read()=>42;}")]
    [InlineData("struct Entry{public readonly int Read()=>42;}")]
    [InlineData("interface I{readonly int Read();}")]
    [InlineData("readonly(int) Read()=>42;")]
    [InlineData("readonly(char*) Read()=>\"text\";")]
    [InlineData("class Entry{public static readonly(int) Read()=>42;}")]
    [InlineData("struct Entry{public static readonly(int) Read()=>42;}")]
    [InlineData("static class Entry{public static readonly(int) Read()=>42;}")]
    [InlineData("int Read(this ref readonly(int) value)=>value;")]
    [InlineData("readonly(int) Value=>42;")]
    [InlineData("class Entry{public static readonly(int) Value{get{return 42;}}}")]
    [InlineData("namespace Lib{public weak readonly(int) Read()=>1;}public replace readonly(int) Lib::Read()=>42;")]
    public void ReturnAndParameterQualifiersRemainIndependent(string source)
        => Assert.Empty(Errors(source));

    [Theory]
    [InlineData("public readonly int Bad()=>42;", "int Healthy(){long actual=42;return (int)actual;}", "Healthy()", 1)]
    [InlineData("static class Entry{public static readonly int Bad()=>42;", "public static int Healthy(){long actual=42;return (int)actual;}}", "Entry::Healthy()", 1)]
    [InlineData("static interface I{readonly int Bad();}", "int Healthy(){long actual=42;return (int)actual;}", "Healthy()", 1)]
    [InlineData("weak readonly int Read()=>MissingDefault();replace readonly int Read()=>42;", "int Healthy(){long actual=42;return (int)actual;}", "Healthy()", 2)]
    public void RecoveryContinuesToBindHealthyBodies(string invalid, string healthy, string call, int errorCount)
    {
        var snapshot = new AnalysisSnapshot([new SourceFile("readonly-recovery.gf", invalid + healthy + "int main()=>" + call + ";")], new() { NoStdLib = true });
        var errors = snapshot.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Equal(errorCount, errors.Length);
        Assert.All(errors, error => Assert.Equal(Message, error.Message));
        var method = Assert.Single(snapshot.Nodes.Select(entry => entry.Node).OfType<MethodDeclaration>(), declaration => declaration.Name == "Healthy");
        var identifier = Assert.Single(snapshot.Nodes.Select(entry => entry.Node).OfType<IdentifierExpression>(), expression => expression.Name == "actual");
        Assert.Equal("long", TypeChecker.TypeName(snapshot.Checker!.GetType(identifier)));
        var invocation = Assert.Single(snapshot.Nodes.Select(entry => entry.Node).OfType<CallExpression>(), expression => expression.Span.Source!.Text.Substring(expression.Span.Start, expression.Span.Length) == call);
        Assert.Same(method, snapshot.Checker.GetResolvedCall(invocation));
    }

    private static Diagnostic[] Errors(string source) => Compiler.Analyze([new SourceFile("readonly-receiver.gf", source)], new() { NoStdLib = true })
        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
}
