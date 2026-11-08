using gflat.CompileExceptions;
using gflat.Preprocessing;

namespace gflat.Tests;

public sealed class DeclarationScopeTests
{
    [Fact]
    public void HeaderDeclarationsKeepTheirIncludingRootIdentity()
    {
        var options = new CompilationOptions { IncludeLoader = path => new SourceFile(path, "int Answer();\n") };
        SourceFile[] sources = [new("implementation.gf", "#scope file\nint Answer()=>42;"),
            new("declaration.gf", "#scope file\n#include \"api.gfh\"\n"), new("main.gf", "int main()=>Answer();")];
        Assert.Throws<TypeCheckException>(() => Compiler.Check(sources, options: options));
        sources[2] = new("main.gf", "#include \"api.gfh\"\nint main()=>Answer();");
        Compiler.Check(sources, options: options);
    }

    [Fact]
    public void GenericPrototypeNeedsABodyForInstantiation()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check([new SourceFile("main.gf", "T Identity<T>(T value); int main()=>Identity(42);")]));
        Assert.Contains("requires a definition", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CallerCanUseDefaultFromItsDeclaration(int optimization)
    {
        SourceFile[] sources = [new("implementation.gf", "#scope file\nint Answer(int value)=>value;"),
            new("main.gf", "#scope file\nint Answer(int value = 42); int main()=>Answer();")];
        Assert.Equal(42, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }

    [Fact]
    public void DefaultFromAnotherScopedFileIsNotVisible()
    {
        SourceFile[] sources = [new("implementation.gf", "int Answer(int value)=>value;"),
            new("declaration.gf", "#scope file\nint Answer(int value = 42);"), new("main.gf", "int main()=>Answer();")];
        Assert.Throws<TypeCheckException>(() => Compiler.Check(sources));
    }

    [Theory]
    [InlineData("file", DeclarationScope.File)]
    [InlineData("project", DeclarationScope.Project)]
    public void DirectiveSetsTheDefiningRootScope(string spelling, DeclarationScope expected)
    {
        var result = new Preprocessor().Process(new SourceFile("scope.gf", "#scope " + spelling + "\nint Answer()=>42;"));
        Assert.Equal(expected, result.DeclarationScope);
    }

    [Theory]
    [InlineData("#scope invalid\n")]
    [InlineData("#scope\n")]
    [InlineData("int Answer()=>42;\n#scope file\n")]
    public void InvalidScopeDirectivesAreRejected(string source)
        => Assert.Throws<TypeCheckException>(() => new Preprocessor().Process(new SourceFile("scope.gf", source)));

    [Fact]
    public void HeaderCannotChangeItsIncludingRootScope()
    {
        var options = new CompilationOptions { IncludeLoader = path => new SourceFile(path, "#scope file\n") };
        Assert.Throws<TypeCheckException>(() => new Preprocessor(options).Process(new SourceFile("scope.gf", "#include \"scope.gfh\"\n")));
    }

    [Fact]
    public void DefinitionIsNotAutomaticallyDeclaredInOtherFiles()
    {
        SourceFile[] sources = [new("implementation.gf", "#scope file\nint Answer()=>42;"), new("main.gf", "int main()=>Answer();")];
        Assert.Throws<TypeCheckException>(() => Compiler.Check(sources));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CallerDeclarationBindsToExternallyLinkedDefinition(int optimization)
    {
        SourceFile[] sources = [new("implementation.gf", "#scope file\nint Answer()=>42;"), new("main.gf", "int Answer(); int main()=>Answer();")];
        Assert.Equal(42, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HiddenOverloadDoesNotParticipateInCallResolution(int optimization)
    {
        SourceFile[] sources = [new("implementation.gf", "#scope file\nint Read(int value)=>1;"), new("main.gf", "int Read(long value)=>42; int main()=>Read(0);")];
        Assert.Equal(42, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FileScopeDoesNotRestrictItsOwnLookupOfOtherFiles(int optimization)
    {
        SourceFile[] sources = [new("implementation.gf", "#scope file\nint Answer()=>Other(); int main()=>Answer();"), new("other.gf", "int Other()=>42;")];
        Assert.Equal(42, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }

    [Fact]
    public void StandalonePrototypeEmitsAnExternalDeclaration()
    {
        string ir = CompilerTestHelper.EmitIr("int Answer(int value); int main()=>Answer(42);");
        Assert.Contains("declare i32 @gflat$Answer(i32", ir);
        Assert.DoesNotContain("define i32 @gflat$Answer(", ir);
    }

    [Fact]
    public void MismatchedPrototypeContractIsRejected()
    {
        SourceFile[] sources = [new("implementation.gf", "int Answer()=>42;"), new("main.gf", "long Answer(); int main()=>0;")];
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(sources));
        Assert.Contains("must match", error.Message);
    }
}
