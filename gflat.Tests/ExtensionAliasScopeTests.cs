using gflat.diagnostics;
namespace gflat.Tests;
public sealed class ExtensionAliasScopeTests
{
    [Theory]
    [InlineData("Selected")]
    [InlineData("Second")]
    [InlineData("readonly(Second)")]
    public void AliasTargetDoesNotCaptureMethodParameter(string selected)
    {
        string source=$$"""
            struct T {public int value;}
            alias Selected=T;
            alias Second=Selected;
            int Read<T>(this {{selected}} first,T second)=>sizeof(T);
            int main(){T value=new T();return value.Read((byte)1);}
            """;
        AssertValid([new SourceFile("extension-alias.gf",source)]);
        Assert.Equal(1,CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void AliasTargetImportsBelongToTheAliasSourceFile()
    {
        SourceFile[] sources = [
            new("types.gf", "namespace A {struct Word{public int value;}} namespace B {struct Word{public long value;}}"),
            new("alias.gf", "using A; alias Selected=Word;"),
            new("extension.gf", "using B; int Read<T>(this Selected first,T second)=>sizeof(T);"),
            new("main.gf", "int main(){A::Word value=new A::Word();return value.Read((byte)1);}")
        ];
        AssertValid(sources);
        Assert.Equal(1,CompilerTestHelper.Run(sources).ExitCode);
    }

    [Fact]
    public void AGlobalAliasTargetTakesPrecedenceOverImportsWithTheSameName()
    {
        const string source="""
            using Imported;
            struct Word {public int value;}
            namespace Imported {struct Word {public long value;}}
            alias Selected=Word;
            int Read<T>(this Selected first,T second)=>sizeof(T);
            int main(){Word value=new Word();return value.Read((byte)1);}
            """;
        AssertValid([new SourceFile("global-precedence.gf",source)]);
        Assert.Equal(1,CompilerTestHelper.Run(source).ExitCode);
    }

    [Fact]
    public void AnUnknownAliasTargetCannotBecomeAMethodParameter()
    {
        const string source="""
            alias Selected=Missing;
            int Read<Missing>(this Selected first,Missing second)=>sizeof(Missing);
            int main(){int value=1;return value.Read(2);}
            """;
        Assert.Contains(Compiler.Analyze([new SourceFile("unknown.gf",source)],new(){NoStdLib=true}),
            d=>d.Severity==DiagnosticSeverity.Error && d.Message.Contains("Missing"));
    }

    private static void AssertValid(SourceFile[] sources) => Assert.DoesNotContain(
        Compiler.Analyze(sources,new(){NoStdLib=true}),d=>d.Severity==DiagnosticSeverity.Error);
}
