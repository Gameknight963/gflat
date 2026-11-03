using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class MacroRenameTests
{
    [Fact]
    public void MacroRenameUpdatesItsBoundUsesAndUndefinition()
    {
        const string code = "#define VALUE 4\n#define WRAP VALUE\nint F() => WRAP + VALUE;\n#undef VALUE\n";
        var source = new SourceFile(Path.GetFullPath("macro-rename.gf"), code);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        Assert.NotNull(model.PrepareRename(source.Path, code.IndexOf("VALUE", StringComparison.Ordinal)));
        Assert.Equal(4, model.Rename(source.Path, code.IndexOf("VALUE", StringComparison.Ordinal), "NUMBER").Length);
    }

    [Fact]
    public void MacroRenameRejectsANameThatCapturesAnUnrelatedIdentifier()
    {
        const string code = "#define VALUE 4\nint number = 1;\nint F() => number + VALUE;";
        var source = new SourceFile(Path.GetFullPath("macro-rename-conflict.gf"), code);
        var model = new EditorModel(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
        Assert.Throws<InvalidOperationException>(() => model.Rename(source.Path, code.IndexOf("VALUE", StringComparison.Ordinal), "number"));
    }
}
