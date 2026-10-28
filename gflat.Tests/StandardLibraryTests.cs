using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.Tests;

public class StandardLibraryTests
{
    [Fact]
    public void AutomaticDefinitionsHaveRealSourceLocations()
    {
        var snapshot = new AnalysisSnapshot([new SourceFile("main.gf", "int main() => 0;")]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == gflat.diagnostics.DiagnosticSeverity.Error);
        foreach (string relative in StandardLibrary.AutomaticFiles)
        {
            var source = Assert.Single(snapshot.Nodes.Select(n => n.Span.Source).OfType<SourceFile>()
                .DistinctBy(s => s.Id), s => StandardLibrary.IsSource(s.Path, relative));
            Assert.True(Path.IsPathRooted(source.Path));
            Assert.True(File.Exists(source.Path));
        }
    }

    [Fact]
    public void ExplicitLibraryInputsAreIncludedExactlyOnce()
    {
        var files = StandardLibrary.AutomaticFiles.Select(StandardLibrary.Load)
            .Append(new SourceFile("main.gf", "int main() => 0;"));
        string ir = Compiler.Emit(files);
        Assert.Equal(1, ir.Split("%Attribute = type").Length - 1);
        Assert.Equal(1, ir.Split("%Exception = type").Length - 1);
        Assert.Equal(1, ir.Split("define i8* @gflat$Memory$Allocate(").Length - 1);
    }

    [Fact]
    public void ExplicitLibrarySnapshotOverridesBundledFile()
    {
        var original = StandardLibrary.Load("core/Attribute.gf");
        var edited = new SourceFile("std/core/Attribute.gf", original.Text + "int LibraryAnswer() => 42;");
        Assert.Equal(42, CompilerTestHelper.Run([edited, new("main.gf", "int main() => LibraryAnswer();")]).ExitCode);
    }

    [Fact]
    public void UnrelatedFileCannotRedefineReservedAttributeBase()
        => Assert.Throws<TypeCheckException>(() => Compiler.Check(new SourceFile("Attribute.gf", "class Attribute {}")));

    [Fact]
    public void DefaultMemoryImplementationLivesInLibc()
    {
        Assert.Contains("namespace Memory", StandardLibrary.Load("libc/Memory/allocation.gf").Text);
        Assert.Contains("extern void*? malloc", StandardLibrary.Load("libc/Memory/allocation.gf").Text);
        foreach (string relative in StandardLibrary.AutomaticFiles.Where(p => p.StartsWith("core/")))
            Assert.DoesNotContain("extern ", StandardLibrary.Load(relative).Text);
    }

    [Fact]
    public void AllCoreSourcesTypeCheckWithoutLibcUsingOnlyExplicitMemoryBindings()
    {
        string core = Path.Combine(AppContext.BaseDirectory, "Std", "core");
        string[] paths = Directory.GetFiles(core, "*.gf", SearchOption.AllDirectories);
        Assert.NotEmpty(paths);
        SourceFile[] sources = paths.Order(StringComparer.Ordinal)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("core-memory-contract.gf", """
                namespace Memory {
                    public void*? Allocate(nuint count) => null;
                    public void Free(void* storage) nothrow { }
                }
                int main() => 0;
                """))
            .ToArray();

        var (unit, _) = Compiler.Check(sources, options: new() { NoStdLib = true });

        // FileUsings records every parsed input, including files without imports.
        // Exact identity excludes any injected standard-library source.
        Assert.True(sources.Select(source => source.Id).ToHashSet().SetEquals(unit.FileUsings.Keys));
    }

    [Fact]
    public void ExplicitLibraryInputsPreserveCustomExceptionAndExternContracts()
    {
        var sources = StandardLibrary.AutomaticFiles.Select(StandardLibrary.Load).Append(new SourceFile("main.gf", """
            class Exception { public int GetCode() => code; public int code; public Exception() { code = 7; } }
            extern void* malloc(nuint size);
            int main() { Exception e = new Exception(); return e.code; }
            """));
        Assert.Equal(7, CompilerTestHelper.Run(sources).ExitCode);
    }

    [Fact]
    public void ExplicitCoreTypesAreEmittedBeforeUserConstants()
    {
        var sources = StandardLibrary.AutomaticFiles.Select(relative => new SourceFile("std/" + relative, StandardLibrary.Load(relative).Text))
            .Prepend(new SourceFile("a.gf", "const Exception* failure = new* Exception(\"test\", 42); int main() => failure.GetCode();"));
        Assert.Equal(42, CompilerTestHelper.Run(sources).ExitCode);
    }

}
