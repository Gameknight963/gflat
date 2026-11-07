using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class CaretSymbolQueryTests
{
    [Theory]
    [InlineData("definition")]
    [InlineData("prepare-rename")]
    [InlineData("rename")]
    [InlineData("references")]
    [InlineData("highlights")]
    [InlineData("hover")]
    public void EscapePrefixSelectsTheSameSymbolAsItsIdentifier(string query)
    {
        const string text = "struct @class {} int F(){int @return=1; @class item=default; return @return;}";
        var source = new SourceFile(Path.GetFullPath("caret-escaped.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        object? Run(int offset) => query switch {
            "definition" => model.Definition(source.Path, offset),
            "prepare-rename" => model.PrepareRename(source.Path, offset),
            "rename" => model.Rename(source.Path, offset, "renamed"),
            "references" => model.References(source.Path, offset, true),
            "highlights" => model.DocumentHighlights(source.Path, offset),
            "hover" => model.Hover(source.Path, offset),
            _ => throw new ArgumentException(query)
        };
        foreach (int prefix in Enumerable.Range(0, text.Length).Where(index => text[index] == '@'))
        {
            var expected = Run(prefix + 1);
            Assert.NotNull(expected);
            Assert.Equal(JsonSerializer.Serialize(expected, Protocol.JsonOptions),
                JsonSerializer.Serialize(Run(prefix), Protocol.JsonOptions));
        }
    }

    [Theory]
    [InlineData("int F(){immutable(char*) text=\"@missing\"; return 0;}", "@missing")]
    [InlineData("int F(){/* @missing */ return 0;}", "@missing")]
    [InlineData("int F(){return @(1);}", "@(1)")]
    public void AtSignsOutsideIdentifiersDoNotSelectASymbol(string text, string marker)
    {
        var source = new SourceFile(Path.GetFullPath("caret-at-sign.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        int offset = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.Null(model.Definition(source.Path, offset));
        Assert.Null(model.PrepareRename(source.Path, offset));
        Assert.Empty(model.DocumentHighlights(source.Path, offset));
    }

    [Theory]
    [InlineData("definition-call", "Read", "Read(1)")]
    [InlineData("definition-type", "Item", "Item item")]
    [InlineData("definition", "local", "local;")]
    [InlineData("prepare-rename", "local", "local;")]
    [InlineData("rename", "local", "local;")]
    [InlineData("references", "local", "local;")]
    [InlineData("highlights", "local", "local;")]
    public void CaretImmediatelyAfterANameSelectsThatSymbol(string query, string name, string marker)
    {
        const string text = "struct Item{} int Read(int parameter)=>parameter; int F(){int local=Read(1); Item item=default;return local;}";
        var source = new SourceFile(Path.GetFullPath("caret-symbol.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        object? Run(int offset) => query switch {
            "definition-call" or "definition-type" or "definition" => model.Definition(source.Path, offset),
            "prepare-rename" => model.PrepareRename(source.Path, offset),
            "rename" => model.Rename(source.Path, offset, "renamed"),
            "references" => model.References(source.Path, offset, true),
            "highlights" => model.DocumentHighlights(source.Path, offset),
            _ => throw new ArgumentException(query)
        };
        int start = text.IndexOf(marker, StringComparison.Ordinal);
        var expected = Run(start);
        Assert.NotNull(expected);
        Assert.Equal(JsonSerializer.Serialize(expected, Protocol.JsonOptions),
            JsonSerializer.Serialize(Run(start + name.Length), Protocol.JsonOptions));
    }

    [Fact]
    public void WhitespaceBetweenTheCaretAndNameDoesNotSelectThePreviousSymbol()
    {
        const string text = "int F(){int local=1;return local ;}";
        var source = new SourceFile(Path.GetFullPath("caret-whitespace.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        int offset = text.LastIndexOf("local", StringComparison.Ordinal) + "local ".Length;
        Assert.Null(model.Definition(source.Path, offset));
        Assert.Null(model.PrepareRename(source.Path, offset));
        Assert.Empty(model.DocumentHighlights(source.Path, offset));
    }

    [Theory]
    [InlineData("#define VALUE 1\nint F()=>VALUE;", "VALUE", "VALUE;")]
    [InlineData("class Base{public virtual int Read()=>1;} class Derived:Base{public override int Read()=>2;}", "override", "override")]
    [InlineData("int F(){int local=1;return local", "local", "local")]
    public void TouchingMacroOverrideAndEndOfFileNamesKeepTheirNavigation(string text, string name, string marker)
    {
        var source = new SourceFile(Path.GetFullPath("caret-special-symbol.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        int start = text.LastIndexOf(marker, StringComparison.Ordinal);
        var expected = model.Definition(source.Path, start);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, start + name.Length));
    }
}
