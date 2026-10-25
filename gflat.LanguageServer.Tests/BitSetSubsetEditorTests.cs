using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class BitSetSubsetEditorTests
{
    [Fact]
    public void InclusionObserversBorrowProtectedOwnersAndNavigateTheirActualMethods()
    {
        const string text = """
            using std; using std::Collections;
            void Observe(immutable(BitSet)* required, immutable(BitSet)* enabled) {
                bool subset = required.IsSubsetOf(enabled);
                bool superset = enabled.IsSupersetOf(required);
            }
            """;
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"StandardLibrary"),"*.gf",SearchOption.AllDirectories)
            .Select(path => new SourceFile(path,File.ReadAllText(path))).ToList();
        var source = new SourceFile(Path.GetFullPath("bitset-subset-editor.gf"),text);
        files.Add(source);
        var library = files.Single(file => Path.GetFileName(file.Path)=="BitSet.gf");
        var snapshot = new AnalysisSnapshot(files,new(){NoStdLib=true});
        Assert.DoesNotContain(snapshot.Diagnostics,error => error.Severity==DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot,files);
        foreach (string name in new[] { "IsSubsetOf", "IsSupersetOf" })
        {
            int use = text.IndexOf(name+"(",StringComparison.Ordinal);
            Assert.True(use>=0);
            Assert.Single(Json(model.Completion(source.Path,use)).GetProperty("items").EnumerateArray(),
                item => item.GetProperty("label").GetString()==name);
            string hover = Json(model.Hover(source.Path,use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("immutable bool",hover);
            Assert.Contains(name,hover);
            Assert.Contains("immutable(BitSet)* other",hover);
            Assert.DoesNotContain("nothrow",hover);
            Assert.DoesNotContain('$',hover);
            string anchor = "public immutable bool "+name+"(";
            int declaration = library.Text.IndexOf(anchor,StringComparison.Ordinal);
            Assert.True(declaration>=0);
            var expected = model.Definition(library.Path,declaration+"public immutable bool ".Length);
            Assert.NotNull(expected);
            Assert.Equal(expected,model.Definition(source.Path,use));
            var help = Json(model.SignatureHelp(source.Path,use+name.Length+1));
            Assert.Equal(0,help.GetProperty("activeParameter").GetInt32());
            var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
            Assert.Equal(1,signature.GetProperty("parameters").GetArrayLength());
            Assert.Contains(name,signature.GetProperty("label").GetString());
            Assert.Contains("other",signature.GetProperty("label").GetString());
        }
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value,Protocol.JsonOptions);
}
