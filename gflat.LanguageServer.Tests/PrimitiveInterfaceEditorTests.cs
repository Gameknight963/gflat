using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class PrimitiveInterfaceEditorTests
{
    [Fact]
    public void PrimitiveConstrainedExtensionsHaveCompletionHoverAndNavigation()
    {
        const string text = """
            interface IHash {
                immutable int Hash();
                implementation int { public immutable int Hash() => *this; }
            }
            extension<T : IHash> T{public immutable int Read()=> (*this).Hash();}
            int main() { int value=42; return value.Read(); }
            """;
        var source = new SourceFile(Path.GetFullPath("primitive-extension.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int method = text.LastIndexOf("Read()", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path, method));
        Assert.NotNull(model.Definition(source.Path, method));
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, method), Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "Read");
    }

    [Fact]
    public void ImplementationBodiesAndConstrainedCallsHaveSemanticInformation()
    {
        const string text = """
            interface IHash {
                immutable int Hash(int extra);
                implementation int { public immutable int Hash(int extra) => *this + extra; }
            }
            int Read<T : IHash>(T n) => n.Hash(2);
            int main() => Read(40);
            """;
        var source = new SourceFile(Path.GetFullPath("primitive-interface.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int method = text.IndexOf("n.Hash", StringComparison.Ordinal) + 2;
        Assert.NotNull(model.Hover(source.Path, method));
        Assert.NotNull(model.Definition(source.Path, method));
        int extra = text.IndexOf("+ extra", StringComparison.Ordinal) + 2;
        Assert.NotNull(model.Hover(source.Path, extra));
        var completion = JsonSerializer.SerializeToElement(model.Completion(source.Path, method), Protocol.JsonOptions);
        Assert.Contains(completion.GetProperty("items").EnumerateArray(), item => item.GetProperty("label").GetString() == "Hash");
    }

    [Fact]
    public void AnInvalidImplementationDoesNotEraseFollowingDeclarations()
    {
        const string text = "interface I { int Read(); implementation int {} } struct Healthy { public int Read() => 42; } int main() { Healthy h = new(); return h.Read(); }";
        var source = new SourceFile(Path.GetFullPath("primitive-recovery.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        Assert.Contains(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        int method = text.LastIndexOf("Read()", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(source.Path, method));
        Assert.NotNull(model.Definition(source.Path, method));
    }
}
