using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class GenericBindingReentryTests
{
    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void NestedMembersKeepTheirOriginalEditorDefinitions(string kind)
    {
        string library = $$"""
            namespace Library {
                public struct Owner { public Family<int>::Item value; }
                public {{kind}} Family<T> {
                    public struct Item { public T number; }
                    public static Owner Make() => new Owner();
                }
            }
            """;
        const string program = """
            using Library;
            int main() {
                Owner owner = Family<int>::Make();
                owner.value.number = 42;
                return owner.value.number;
            }
            """;
        var declaration = new SourceFile(Path.GetFullPath("nested-member-library.gf"), library);
        var caller = new SourceFile(Path.GetFullPath("nested-member-main.gf"), program);
        var snapshot = new AnalysisSnapshot([declaration, caller]);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [declaration, caller]);
        int use = program.LastIndexOf("number", StringComparison.Ordinal);
        int field = library.IndexOf("number", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(caller.Path, use));
        Assert.Equal(model.Definition(declaration.Path, field), model.Definition(caller.Path, use));
        Assert.NotNull(model.Definition(caller.Path, use));
        Assert.Equal(4, snapshot.Checker!.GetStructSize(snapshot.Checker.GetStruct("Library::Owner")!));
    }

    [Fact]
    public void ExpandingFunctionsLeaveUnrelatedEditorBindingsUsable()
    {
        const string text = """
            void Expand<T>() { Expand<T*>(); }
            int Broken() { Expand<int>(); return 0; }
            struct Box<T> { public T value; }
            int Healthy() { Box<int> box = default; box.value = 42; return box.value; }
            """;
        var source = new SourceFile(Path.GetFullPath("function-expansion-recovery.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Message.Contains("compiler nesting limit", StringComparison.Ordinal));
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Span.Start >= text.IndexOf("struct Box", StringComparison.Ordinal));
        var model = new EditorModel(snapshot, [source]);
        int use = text.LastIndexOf("value", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path, use));
        Assert.NotNull(model.Definition(source.Path, use));
    }

    [Theory]
    [InlineData("Box<int>", 4)]
    [InlineData("Box<int>*?", 8)]
    [InlineData("Box<int>[2]", 8)]
    [InlineData("BoxInt", 4)]
    [InlineData("readonly(BoxInt)", 4)]
    public void GenericBodiesCanQueryContainingLayoutsWithoutBreakingEditorBindings(string fieldType, int size)
    {
        string alias = fieldType.Contains("BoxInt", StringComparison.Ordinal) ? "public alias BoxInt = Box<int>;" : "";
        string text = $$"""
            namespace Demo {
                {{alias}}
                public struct Owner { public {{fieldType}} value; }
                public struct Box<T> {
                    public T number;
                    public static Owner Make() => new Owner();
                }
                public int Read() {
                    Owner owner = Box<int>::Make();
                    owner.value;
                    return sizeof(Owner);
                }
            }
            """;
        var source = new SourceFile(Path.GetFullPath("generic-reentry.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int use = text.IndexOf("owner.value", StringComparison.Ordinal) + "owner.".Length;
        int declaration = text.IndexOf("value;", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path, use));
        Assert.Equal(model.Definition(source.Path, declaration), model.Definition(source.Path, use));
        Assert.NotNull(model.Definition(source.Path, use));
        int typeUse = text.LastIndexOf("Owner", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path, typeUse, options: new HoverOptions(ShowTypeLayout: true)));
        Assert.Equal(size, snapshot.Checker!.GetStructSize(snapshot.Checker.GetStruct("Demo::Owner")!));
    }
}
