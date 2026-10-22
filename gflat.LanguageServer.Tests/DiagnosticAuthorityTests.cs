using gflat.diagnostics;
using gflat.TestSupport;

namespace gflat.LanguageServer.Tests;

public sealed class DiagnosticAuthorityTests
{
    [Theory]
    [InlineData("namespace Lib{enum E{A}} int main()=>Lib::E::Missing;", "Enum 'Lib::E' does not contain member 'Missing'", "Lib::E::Missing")]
    [InlineData("namespace Lib{struct Box<T>{public T value;}} int main(){Lib::Box<int> a=new Lib::Box<int>();return (int)a;}", "Cannot cast 'Lib::Box<int>' to 'int'", "(int)a")]
    [InlineData("namespace Lib{struct A{} struct B{}} void F(Lib::A* p){p.~Lib::B();} int main()=>0;", "Destructor type 'Lib::B' must match the pointed-to type 'Lib::A'", "p.~Lib::B()")]
    [InlineData("namespace Lib{struct Box<T>{public T* item;}} int main(){Lib::Box<int> value=new Lib::Box<int>();return 0;}", "Field 'Lib::Box<int>.item' requires an initializer or constructor assignment", "new Lib::Box<int>()")]
    [InlineData("namespace Lib{struct Family<T>{public struct Item{public T value;}}} int main(){immutable(Lib::Family<int>::Item) a=new Lib::Family<int>::Item();return (int)a;}", "Cannot cast 'immutable(Lib::Family<int>::Item)' to 'int'", "(int)a")]
    [InlineData("namespace Lib{int F<T>()=>0;} const int value=Lib::F<int>(); int main()=>0;", "Const variable 'value' initializer must be a compile-time constant: Function 'Lib::F<int>' is not marked 'const' and cannot be evaluated at compile time", "const int value=Lib::F<int>();")]
    [InlineData("namespace Lib{class Outer{public class Inner{}} struct Box<T>{public T value;} Box<Outer::Inner*> Read(){return 1;}}", "Cannot return 'int' from function returning 'Lib::Box<Lib::Outer::Inner*>'", "return 1;")]
    public void CompilerSnapshotAndProjectWorkspacePublishIdenticalDiagnosticTextAndRanges(string text, string expected, string offending)
    {
        string root = Path.Combine(Path.GetTempPath(), "gflat-diagnostic-authority-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "app.gfproj"), ProjectFileFixture.Xml());
            var source = new SourceFile(Path.Combine(root, "main.gf"), text);
            File.WriteAllText(source.Path, text);
            var compiler = Assert.Single(Compiler.Analyze([source], new() { NoStdLib = true }), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
            var analyzed = Assert.Single(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var documents = new Dictionary<string, OpenDocument>(Workspace.Paths) { [source.Path] = new(source.Path, text, 1) };
            var workspace = Workspace.Analyze(root, documents);
            var published = Assert.Single(workspace.Diagnostics[source.Path], diagnostic => diagnostic.Severity == 1);
            Assert.Equal(expected, compiler.Message);
            Assert.Equal(compiler.Message, analyzed.Message);
            Assert.Equal(compiler.Message, published.Message);
            Assert.Equal(compiler.Descriptor.Id, published.Code);
            Assert.Equal(source.Path, compiler.FilePath);
            Assert.Equal(compiler.Span.Start, analyzed.Span.Start);
            Assert.Equal(compiler.Span.Length, analyzed.Span.Length);
            Assert.Equal(offending, text.Substring(compiler.Span.Start, compiler.Span.Length));
            var start = source.Span(compiler.Span.Start);
            var end = source.Span(compiler.Span.Start + compiler.Span.Length);
            Assert.Equal(new TextRange(new(start.Line - 1, start.Column - 1), new(end.Line - 1, end.Column - 1)), published.Range);
        }
        finally { Directory.Delete(root, true); }
    }
}
