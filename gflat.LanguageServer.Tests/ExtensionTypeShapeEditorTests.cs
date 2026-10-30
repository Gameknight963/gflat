using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class ExtensionTypeShapeEditorTests
{
    [Theory]
    [InlineData("int(int)")]
    [InlineData("int(int)*")]
    [InlineData("int(int)*?")]
    public void FunctionTypeArgumentsPreserveExtensionCompletion(string type)
    {
        string marked = $$"""
            struct Handler<F>{public int value;}
            extension Handler<{{type}}>{public int Read<T>(T value)=>42;}
            int main(){Handler<{{type}}> handler=new Handler<{{type}}>();handler.|;return 0;}
            """;
        int offset = marked.IndexOf('|');
        var source = new SourceFile(Path.GetFullPath("extension-function-shape.gf"), marked.Remove(offset, 1));
        var snapshot = new AnalysisSnapshot([source]);
        var model = new EditorModel(snapshot, [source]);
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, offset), Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "Read");
    }
}
