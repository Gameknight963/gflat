using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class GenericHierarchyEditorTests
{
    [Fact]
    public void GenericClassInheritsFieldsAndInterfacesFromItsDeclarationContext()
    {
        const string library = """
            namespace Library {
                public interface IValue { int Read(); }
                public class Base { public long first; public virtual int Read() => 1; }
                public class Box<T> : Base, IValue {
                    public T second;
                    public override int Read() => 42;
                }
            }
            """;
        const string program = """
            using Library;
            int main() {
                Box<int> box = new Box<int>();
                IValue* view = &box;
                box.first = 0;
                return view.Read();
            }
            """;
        var declaration = new SourceFile(Path.GetFullPath("generic-hierarchy-library.gf"), library);
        var caller = new SourceFile(Path.GetFullPath("generic-hierarchy-main.gf"), program);
        var snapshot = new AnalysisSnapshot([declaration, caller]);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [declaration, caller]);
        int use = program.IndexOf("first", StringComparison.Ordinal);
        int field = library.IndexOf("first", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(caller.Path, use));
        Assert.Equal(model.Definition(declaration.Path, field), model.Definition(caller.Path, use));
        Assert.NotNull(model.Definition(caller.Path, use));
        Assert.NotNull(model.Hover(caller.Path, program.LastIndexOf("Read", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void GenericInterfaceImplementationsKeepWorkingAcrossFiles(string kind)
    {
        string library = $$"""
            namespace Library {
                public interface IValue { int Read(); }
                public {{kind}} Box<T> : IValue { public int Read() => 42; }
            }
            """;
        const string program = """
            int main() {
                Library::Box<int> box = new Library::Box<int>();
                Library::IValue* view = &box;
                return view.Read();
            }
            """;
        var declaration = new SourceFile(Path.GetFullPath("generic-interface-library.gf"), library);
        var caller = new SourceFile(Path.GetFullPath("generic-interface-main.gf"), program);
        var snapshot = new AnalysisSnapshot([declaration, caller]);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [declaration, caller]);
        int use = program.LastIndexOf("Read", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(caller.Path, use));
        Assert.NotNull(model.Definition(caller.Path, use));
    }
}
