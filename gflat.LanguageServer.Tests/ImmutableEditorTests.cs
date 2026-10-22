using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class ImmutableEditorTests
{
    private const string Library = """
        struct Buffer {
            public int* data;
            public immutable int Read()=>data[0];
            public readonly int Shallow(){data[0]=1;return data[0];}
            public int Mutate(){data[0]=2;return data[0];}
            public int Value{immutable get=>data[0];}
        }
        int DeepExtension(this ref immutable(Buffer) value)=>value.Read();
        int ShallowExtension(this ref readonly(Buffer) value)=>value.Shallow();
        """;

    [Theory]
    [InlineData("Buffer", true, true)]
    [InlineData("readonly(Buffer)", true, false)]
    [InlineData("immutable(Buffer)", false, false)]
    public void MemberCompletionsRespectReceiverContracts(string type, bool shallow, bool mutable)
    {
        var (model, source) = Model(Library + "int Probe(" + type + "* view)=>view.Read();");
        int position = source.Text.LastIndexOf("view.Read", StringComparison.Ordinal) + "view.".Length;
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, position), Protocol.JsonOptions);
        var names = completion.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("label").GetString()).ToArray();
        Assert.Contains("Read", names);
        Assert.Contains("DeepExtension", names);
        Assert.Equal(shallow, names.Contains("Shallow"));
        Assert.Equal(shallow, names.Contains("ShallowExtension"));
        Assert.Equal(mutable, names.Contains("Mutate"));
    }

    [Fact]
    public void HoverAndSignatureHelpShowImmutableReceiverAndAccessor()
    {
        var (model, source) = Model(Library + "int Probe(immutable(Buffer)* view){return view.Read()+view.Value;}");
        int call = source.Text.LastIndexOf("view.Read", StringComparison.Ordinal) + "view.".Length;
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, call), Protocol.JsonOptions);
        string signature = hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("immutable int", signature);
        Assert.DoesNotContain("readonly immutable", signature);
        var help = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, call + "Read(".Length), Protocol.JsonOptions);
        Assert.Contains("immutable int", help.GetProperty("signatures")[0].GetProperty("label").GetString());
        var property = JsonSerializer.SerializeToElement(model.Hover(source.Path, source.Text.LastIndexOf("Value", StringComparison.Ordinal)), Protocol.JsonOptions);
        Assert.Contains("immutable get", property.GetProperty("contents").GetProperty("value").GetString());
        Assert.NotNull(model.Definition(source.Path, call));
    }

    [Fact]
    public void CompletionOffersImmutableKeyword()
    {
        var (model, source) = Model("struct Buffer{}\n");
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, source.Text.Length), Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "immutable");
    }

    [Fact]
    public void ImmutableAccessorKeepsKeywordClassification()
    {
        var (model, source) = Model(Library);
        var expected = source.Span(source.Text.IndexOf("get=>", StringComparison.Ordinal));
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int line = 0, column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            line += data[i];
            column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            if (line == expected.Line - 1 && column == expected.Column - 1)
            {
                Assert.Equal("modifier", EditorModel.TokenTypes[data[i + 3]]);
                return;
            }
        }
        Assert.Fail("The immutable getter keyword was not classified.");
    }

    private static (EditorModel Model, SourceFile Source) Model(string text)
    {
        var source = new SourceFile(Path.GetFullPath("immutable-editor.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return (new EditorModel(snapshot, [source]), source);
    }
}
