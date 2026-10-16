using gflat.TestSupport;

namespace gflat.LanguageServer.Tests;

public sealed class ReferenceContextTests
{
    [Fact]
    public void ReferenceConditionalSourcesAndImportsParticipateInEditorAnalysis()
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-reference-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "app"));
        Directory.CreateDirectory(Path.Combine(root, "lib"));
        try
        {
            string project = Path.GetFullPath(Path.Combine(root, "app/app.gfproj"));
            File.WriteAllText(project, ProjectFileFixture.Xml(content: "<ItemGroup><ProjectReference Include=\"../lib/lib.gfproj\" /></ItemGroup>"));
            string main = Path.GetFullPath(Path.Combine(root, "app/main.gf"));
            File.WriteAllText(main, "int main()=>Answer();");
            File.WriteAllText(Path.Combine(root, "lib/lib.gfproj"), ProjectFileFixture.Xml("SourceLibrary", """
                <PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
                <Import Project="reference.props" Condition="'$(GflatAsReference)' == 'true'" />
                """));
            string import = Path.GetFullPath(Path.Combine(root, "lib/reference.props"));
            File.WriteAllText(import, "<Project><ItemGroup><Compile Include=\"reference.gf\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(root, "lib/reference.gf"), "int Answer()=>42;");
            var state = new ProjectWorkspace();
            var loaded = state.Load(project);
            Assert.Contains(import, loaded.Paths);
            var open = new Dictionary<string, OpenDocument>(Workspace.Paths) { [main] = new(main, File.ReadAllText(main), 1) };
            var analysis = Workspace.Analyze(root, open, state);
            Assert.Empty(analysis.Diagnostics[main]);
        }
        finally { Directory.Delete(root, true); }
    }
}
