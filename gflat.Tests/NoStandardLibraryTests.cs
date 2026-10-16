using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.Tests;

public class NoStandardLibraryTests
{
    private static readonly CompilationOptions Bare = new() { NoStdLib = true };

    [Fact]
    public void CheckingDoesNotInjectAnyLibrarySources()
    {
        var (unit, _) = Compiler.Check("class Attribute {} class Exception {} int main() => 0;", options: Bare);
        Assert.Equal(3, unit.Members.Count);
        Assert.All(unit.Members, member => Assert.Equal("<input>", member.Span.Source!.Path));
    }

    [Fact]
    public void EmissionWorksWithoutMemoryOrExceptionRuntimeDeclarations()
    {
        string ir = Compiler.Emit("int main() => 42;", options: Bare);
        Assert.Contains("ret i32 42", ir);
        Assert.DoesNotContain("gflat$Memory$Allocate", ir);
        Assert.DoesNotContain("%Exception = type", ir);
    }

    [Fact]
    public void LibraryNamesAreUnavailableUnlessSuppliedExplicitly()
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Check("int main() { Exception value; return 0; }", options: Bare));
        Compiler.Check("class Example {} int main() { Example value; return 0; }", options: Bare);
    }

    [Fact]
    public void EditorAnalysisUsesTheSameInputPolicy()
    {
        var snapshot = new AnalysisSnapshot([new SourceFile("main.gf", "class Attribute {} int main() => 0;")], Bare);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.All(snapshot.Nodes.Where(n => n.Span.Source != null), n => Assert.Equal("main.gf", n.Span.Source!.Path));
    }
    [Fact]
    public void DirectCliFlagExcludesLibraryDeclarations()
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-bare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "main.gf");
            string output = Path.Combine(root, "main.ll");
            File.WriteAllText(source, "class Attribute {} int main() => 42;");
            Assert.Equal(0, Program.Main([source, "--no-stdlib", "--emit-ir", "-o", output]));
            Assert.DoesNotContain("%Exception = type", File.ReadAllText(output));
            Assert.Equal(0, Program.Main([source, "--no-stdlib", "--check"]));
            Assert.Equal(42, Program.Main([source, "--no-stdlib", "--run", "-o", Path.Combine(root, "bare.exe")]));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ProjectCliOverrideDoesNotReinsertStdDuringTheBuild()
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-bare-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "app.gfproj");
            File.WriteAllText(project, "<Project Sdk=\"Gflat.Sdk\" />");
            File.WriteAllText(Path.Combine(root, "main.gf"), "class Attribute {} int main() => 42;");
            Assert.Equal(0, Program.Main(["build", project, "--no-stdlib", "--emit-ir"]));
            Assert.DoesNotContain("%Exception = type", File.ReadAllText(Path.Combine(root, "bin/Debug/app.ll")));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void CleanPreservesTheCliStdExclusionInAStdFreeToolchain()
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-bare-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "toolchain/build"));
        try
        {
            string toolchain = Path.Combine(root, "toolchain");
            foreach (string name in new[] { "Gflat.props", "Gflat.targets" })
                File.Copy(Path.Combine(TestSupport.ProjectFileFixture.RepositoryRoot, "build", name), Path.Combine(toolchain, "build", name));
            string project = Path.Combine(root, "app.gfproj");
            File.WriteAllText(project, new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Sdk", "Gflat.Sdk"),
                new System.Xml.Linq.XElement("PropertyGroup", new System.Xml.Linq.XElement("GflatSdkRoot", toolchain))).ToString());
            File.WriteAllText(Path.Combine(root, "main.gf"), "int main() => 0;");
            Assert.Equal(0, Program.Main(["build", project, "--no-stdlib", "--emit-ir"]));
            string output = Path.Combine(root, "bin/Debug/app.ll");
            Assert.True(File.Exists(output));
            Assert.Equal(0, Program.Main(["clean", project, "--no-stdlib"]));
            Assert.False(File.Exists(output));
        }
        finally { Directory.Delete(root, true); }
    }
}
