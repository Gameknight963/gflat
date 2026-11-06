using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class IdentifierCompletionReuseTests
{
    private static (SourceFile Source, EditorModel Model) Model(string text)
    {
        var source = new SourceFile(Path.GetFullPath("identifier-reuse.gf"), text);
        return (source, new(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]));
    }

    [Theory]
    [InlineData("int Read(){int local=1;return lo;}", "int Read(){int local=1;return loc|;}")]
    [InlineData("void Read(){int local=1;local;}", "void Read(){int local=1;loc|;}")]
    [InlineData("void Read(){int local=1; }", "void Read(){int local=1; loc|}")]
    [InlineData("int Read(){int local=1;return ;}", "int Read(){int local=1;return loc|;}")]
    public void ExpressionPrefixesReuseScopeAndMatchCurrentAnalysis(string before, string marked)
    {
        var (source, model) = Model(before);
        int offset = marked.IndexOf('|');
        string changed = marked.Remove(offset, 1);
        Assert.True(model.TryReuseIdentifierCompletion(source.Path, changed, offset, out var reused));
        var completion = JsonSerializer.SerializeToElement(reused, Protocol.JsonOptions);
        var local = Assert.Single(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "local");
        Assert.Equal("local", local.GetProperty("textEdit").GetProperty("newText").GetString());
        Assert.Equal(offset, local.GetProperty("textEdit").GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
        var (_, current) = Model(changed);
        var fresh = JsonSerializer.SerializeToElement(current.Completion(source.Path, offset), Protocol.JsonOptions);
        Assert.Equal(Labels(fresh), Labels(completion));
    }

    [Theory]
    [InlineData("void Read(){int local=1;}", "void Read(){int loc|=1;}")]
    [InlineData("void Read(){Thing* local;}", "void Read(){Thin|* local;}")]
    [InlineData("void Read(){int local=1;lo;}", "void Read(){int local=2;loc|;}")]
    [InlineData("void Read(){lo;}", "void Read(){loc|();}")]
    [InlineData("void Read(){item.Alpha;}", "void Read(){item.Al|;}")]
    [InlineData("void Read(){// lo\n}", "void Read(){// loc|\n}")]
    [InlineData("void Read(){readonly(char)* value=\"lo\";}", "void Read(){readonly(char)* value=\"loc|\";}")]
    [InlineData("int local;", "int loc|;")]
    [InlineData("void Read(){\n#define ITEM lo\n}", "void Read(){\n#define ITEM loc|\n}")]
    [InlineData("void Read(){\n#define ITEM \\\nlo\n}", "void Read(){\n#define ITEM \\\nloc|\n}")]
    [InlineData("#define CLOSE }\nvoid Read(){CL;}", "#define CLOSE }\nvoid Read(){CLOSE|;}")]
    [InlineData("int Read(){var value=Helper();return 0;} int Helper()=>1; bool Other()=>true;", "int Read(){var value=Other|();return 0;} int Helper()=>1; bool Other()=>true;")]
    [InlineData("const int Read(){int local=1;return lo;}", "const int Read(){int local=1;return loc|;}")]
    [InlineData("void Read(){int local=1;Use(()=>lo);}", "void Read(){int local=1;Use(()=>loc|);}")]
    [InlineData("void Read(){int local=1;static if(true){lo;}}", "void Read(){int local=1;static if(true){loc|;}}")]
    [InlineData("struct Item {public int local;public Item(){lo;}}", "struct Item {public int local;public Item(){loc|;}}")]
    public void ChangedBindingsAndNonExpressionTextRequireAnalysis(string before, string marked)
    {
        var (source, model) = Model(before);
        int offset = marked.IndexOf('|');
        Assert.False(model.TryReuseIdentifierCompletion(source.Path, marked.Remove(offset, 1), offset, out _));
    }

    private static string[] Labels(JsonElement completion) => completion.GetProperty("items").EnumerateArray()
        .Select(item => item.GetProperty("label").GetString()!).Order(StringComparer.Ordinal).ToArray();

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void EarlierDirectiveDoesNotPreventOrdinaryExpressionReuse(string newline)
    {
        string before = "#define FLAG 1" + newline + "int Read(){int local=1;return lo;}";
        var (source, model) = Model(before);
        string changed = before.Replace("return lo;", "return loc;", StringComparison.Ordinal);
        int offset = changed.IndexOf("loc;", StringComparison.Ordinal) + 3;
        Assert.True(model.TryReuseIdentifierCompletion(source.Path, changed, offset, out _));
    }
}
