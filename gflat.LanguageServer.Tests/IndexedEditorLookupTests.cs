using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public class IndexedEditorLookupTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void DeclarationIndexesKeepIdenticalPositionsInDifferentFilesSeparate(string kind)
    {
        var first = new SourceFile(Path.GetFullPath("indexed/first.gf"), $"namespace A; {kind} Box {{ public int Value; }}");
        var second = new SourceFile(Path.GetFullPath("indexed/second.gf"), $"namespace B; {kind} Box {{ public int Value; }}");
        SourceFile[] files = [first, second];
        var model = new EditorModel(new AnalysisSnapshot(files), files);
        Assert.Equal(first.Text.IndexOf("Value", StringComparison.Ordinal), second.Text.IndexOf("Value", StringComparison.Ordinal));
        foreach (var file in files)
        foreach (string name in new[] { "Box", "Value" })
        {
            int offset = file.Text.IndexOf(name, StringComparison.Ordinal);
            string definition = JsonSerializer.Serialize(model.Definition(file.Path, offset), Protocol.JsonOptions);
            Assert.Contains(Workspace.FileUri(file.Path), definition);
            Assert.DoesNotContain(Workspace.FileUri(ReferenceEquals(file, first) ? second.Path : first.Path), definition);
        }
    }

    [Fact]
    public void NameFilteredReferencesPreserveParameterMemberAndInheritedPriorityWhileCompletionKeepsAllNames()
    {
        const string text = """
            class Base { public double Value; protected int Visible; private int Hidden; }
            class Child : Base {
                private int Value;
                int Read(short Value) { return (int)Value; }
                int Own() { return Value + Visible; }
                void Complete() { }
            }
            """;
        var source = new SourceFile(Path.GetFullPath("indexed/scopes.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source]), [source]);
        int parameterUse = text.IndexOf("(int)Value", StringComparison.Ordinal) + 5;
        int memberUse = text.IndexOf("Value +", StringComparison.Ordinal);
        int inheritedUse = text.IndexOf("Visible;", text.IndexOf("int Own", StringComparison.Ordinal), StringComparison.Ordinal);
        string Hover(int offset) => JsonSerializer.SerializeToElement(model.Hover(source.Path, offset), Protocol.JsonOptions)
            .GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("short Value", Hover(parameterUse));
        Assert.Contains("int Child.Value", Hover(memberUse));
        Assert.Contains("int Base.Visible", Hover(inheritedUse));
        var labels = JsonSerializer.SerializeToElement(model.Completion(source.Path, text.IndexOf("void Complete() {", StringComparison.Ordinal) + "void Complete() {".Length), Protocol.JsonOptions)
            .GetProperty("items").EnumerateArray().Select(item => item.GetProperty("label").GetString()).ToArray();
        Assert.Contains("Value", labels);
        Assert.Contains("Visible", labels);
        Assert.Contains("Read", labels);
        Assert.Contains("Own", labels);
        Assert.DoesNotContain("Hidden", labels);
    }
}
