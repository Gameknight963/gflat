using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class AutoGetterEditorTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void WritablePointerAutoPropertiesKeepAliasesAndSourceNavigation(string kind)
    {
        string library = $$"""
            namespace Library {
                alias Address=int*;
                public {{kind}} Holder {
                    public Address Value { get; }
                    public Holder(Address value) { Value=value; }
                }
            }
            """;
        const string program = """
            alias Address=long;
            int main() {
                int number=1;
                Library::Holder holder=new Library::Holder(&number);
                *holder.Value=7;
                return number;
            }
            """;
        var source = new SourceFile(Path.GetFullPath("auto-getter-library.gf"), library);
        var caller = new SourceFile(Path.GetFullPath("auto-getter-main.gf"), program);
        var snapshot = new AnalysisSnapshot([caller, source], new() { NoStdLib=true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [caller, source]);
        int declaration = library.IndexOf("Value {", StringComparison.Ordinal);
        int use = program.IndexOf("Value=7", StringComparison.Ordinal);
        var expected = model.Definition(source.Path, declaration);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(caller.Path, use));
        var hover = JsonSerializer.SerializeToElement(model.Hover(caller.Path, use), Protocol.JsonOptions);
        string text = hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("Address", text);
        Assert.DoesNotContain("long", text);
        Assert.DoesNotContain('$', text);
    }
}
