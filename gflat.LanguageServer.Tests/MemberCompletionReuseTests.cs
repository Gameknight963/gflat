using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class MemberCompletionReuseTests
{
    private const string Prefix = "struct Item { public int Alpha; public void Append() {} } int main() { Item item = new(); ";

    private static EditorModel Model(string expression)
    {
        var source = new SourceFile("main.gf", Prefix + expression + " return 0; }");
        return new(new AnalysisSnapshot([source], new() { NoStdLib = true }), [source]);
    }

    [Theory]
    [InlineData("item.Alpha;", "item.A;")]
    [InlineData("item.A;", "item.Append;")]
    [InlineData("item.Alpha;", "item.;")]
    [InlineData("item;", "item.;")]
    [InlineData("item;", "item.Ap;")]
    public void OnlyMemberNameOrSeparatorEditsReuseCheckedReceiver(string before, string after)
    {
        var model = Model(before);
        string text = Prefix + after + " return 0; }";
        int offset = Prefix.Length + after.IndexOf(';');
        Assert.True(model.TryReuseMemberCompletion("main.gf", text, offset, out var result));
        var completion = JsonSerializer.SerializeToElement(result, Protocol.JsonOptions);
        var append = Assert.Single(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "Append");
        var range = append.GetProperty("textEdit").GetProperty("range");
        Assert.Equal(Prefix.Length + "item.".Length, range.GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(offset, range.GetProperty("end").GetProperty("character").GetInt32());
        Assert.Equal("Append", append.GetProperty("textEdit").GetProperty("newText").GetString());
    }

    [Theory]
    [InlineData("other.A;")]
    [InlineData("item.A; int other = 1;")]
    [InlineData("item.A();")]
    [InlineData("other.Alpha.A;")]
    public void ReceiverAndSurroundingEditsRequireCurrentAnalysis(string after)
    {
        var model = Model("item.Alpha;");
        string text = Prefix + after + " return 0; }";
        Assert.False(model.TryReuseMemberCompletion("main.gf", text, Prefix.Length + after.IndexOf(';'), out _));
    }

    [Fact]
    public void InsertedDotOffersReflectionMembers()
    {
        const string prefix = "int main() { const reflection info = reflect(int); ";
        var source = new SourceFile("main.gf", prefix + "info; return 0; }");
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        var model = new EditorModel(snapshot, [source]);
        string text = prefix + "info.Na; return 0; }";
        Assert.True(model.TryReuseMemberCompletion(source.Path, text, prefix.Length + "info.Na".Length, out var result));
        var completion = JsonSerializer.SerializeToElement(result, Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "Name");
    }

    [Fact]
    public void MemberLikeTextInsideCommentsIsNotReused()
    {
        var model = Model("// item.Alpha;\n");
        string text = Prefix + "// item.A;\n return 0; }";
        Assert.False(model.TryReuseMemberCompletion("main.gf", text, Prefix.Length + "// item.A".Length, out _));
    }
}
