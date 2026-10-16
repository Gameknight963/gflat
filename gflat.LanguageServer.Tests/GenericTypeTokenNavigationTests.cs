using gflat.diagnostics;
using System.Text.Json;

namespace gflat.LanguageServer.Tests;

public sealed class GenericTypeTokenNavigationTests
{
    private const string Library = "namespace Library {public struct Arg{} public class Family<T>{public struct Item{}}}";

    [Theory]
    [InlineData("Library::Family<Library::Arg>::Item")]
    [InlineData("Family<Arg>::Item")]
    [InlineData("readonly(Library::Family<Library::Arg>::Item)*?")]
    public void GenericTypeSegmentsNavigateToTheirOwnDeclarations(string type)
    {
        string text = "using Library; void Use(" + type + " value) {} int main()=>0;";
        var library = new SourceFile(Path.GetFullPath("type-token-library.gf"), Library);
        var caller = new SourceFile(Path.GetFullPath("type-token-caller.gf"), text);
        var snapshot = new AnalysisSnapshot([library, caller]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [library, caller]);
        foreach (string name in new[] { "Family", "Arg", "Item" })
        {
            int use = text.IndexOf(name, StringComparison.Ordinal);
            int declaration = Library.IndexOf(name, StringComparison.Ordinal);
            Assert.NotNull(model.Hover(caller.Path, use));
            Assert.NotNull(model.Definition(caller.Path, use));
            Assert.Equal(model.Definition(library.Path, declaration), model.Definition(caller.Path, use));
            Assert.Equal(name == "Family" ? "class" : "struct", Classification(model, caller.Path, use));
        }
    }

    [Fact]
    public void AFailedDeclarationDoesNotHideHealthyGenericTypeTokens()
    {
        const string text = "using Library; void Broken(Missing item){} void Use(Family<Arg>::Item value){} int main()=>0;";
        var library = new SourceFile(Path.GetFullPath("recovery-token-library.gf"), Library);
        var caller = new SourceFile(Path.GetFullPath("recovery-token-caller.gf"), text);
        var snapshot = new AnalysisSnapshot([library, caller]);
        Assert.Contains(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [library, caller]);
        int use = text.IndexOf("Item value", StringComparison.Ordinal);
        Assert.NotNull(model.Definition(caller.Path, use));
        Assert.Equal(model.Definition(library.Path, Library.IndexOf("Item", StringComparison.Ordinal)), model.Definition(caller.Path, use));
    }

    [Fact]
    public void QualifiedSegmentsIgnoreUnrelatedCallerAliases()
    {
        const string text = "int main(){alias Family=byte;alias Arg=byte;alias Item=byte;Library::Family<Library::Arg>::Item item=default;return 0;}";
        var library = new SourceFile(Path.GetFullPath("shadow-token-library.gf"), Library);
        var caller = new SourceFile(Path.GetFullPath("shadow-token-caller.gf"), text);
        var snapshot = new AnalysisSnapshot([library, caller]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [library, caller]);
        foreach (string name in new[] { "Family", "Arg", "Item" })
            Assert.Equal(model.Definition(library.Path, Library.IndexOf(name, StringComparison.Ordinal)),
                model.Definition(caller.Path, text.LastIndexOf(name, StringComparison.Ordinal)));
    }

    [Fact]
    public void AliasAndUninstantiatedTypeParameterTokensKeepTheirOwnIdentity()
    {
        const string text = "using Library;alias Selected=Family<Arg>;void Use(Selected::Item value){} void Generic<T>(Family<T>::Item value){} int main()=>0;";
        var library = new SourceFile(Path.GetFullPath("alias-token-library.gf"), Library);
        var caller = new SourceFile(Path.GetFullPath("alias-token-caller.gf"), text);
        var snapshot = new AnalysisSnapshot([library, caller]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [library, caller]);
        int aliasDeclaration = text.IndexOf("Selected", StringComparison.Ordinal);
        int aliasUse = text.IndexOf("Selected::", StringComparison.Ordinal);
        Assert.NotNull(model.Definition(caller.Path, aliasUse));
        Assert.Equal(model.Definition(caller.Path, aliasDeclaration), model.Definition(caller.Path, aliasUse));
        int parameterDeclaration = text.IndexOf("T>", StringComparison.Ordinal);
        int parameterUse = text.LastIndexOf("T>", StringComparison.Ordinal);
        Assert.NotNull(model.Definition(caller.Path, parameterUse));
        Assert.Equal(model.Definition(caller.Path, parameterDeclaration), model.Definition(caller.Path, parameterUse));
        Assert.Equal("typeParameter", Classification(model, caller.Path, parameterUse));
    }

    [Fact]
    public void AnIncompleteGenericSuffixDoesNotBreakFollowingTypeNavigation()
    {
        const string text = "void Broken(Library::Family<Library::Arg>::);void Good(Library::Family<Library::Arg>::Item value){} int main()=>0;";
        var library = new SourceFile(Path.GetFullPath("incomplete-token-library.gf"), Library);
        var caller = new SourceFile(Path.GetFullPath("incomplete-token-caller.gf"), text);
        var snapshot = new AnalysisSnapshot([library, caller]);
        Assert.Contains(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [library, caller]);
        int use = text.IndexOf("Item value", StringComparison.Ordinal);
        Assert.NotNull(model.Hover(caller.Path, use));
        Assert.NotNull(model.Definition(caller.Path, use));
        Assert.Equal(model.Definition(library.Path, Library.IndexOf("Item", StringComparison.Ordinal)), model.Definition(caller.Path, use));
    }

    private static string? Classification(EditorModel model, string path, int offset)
    {
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(path)).GetProperty("data")
            .EnumerateArray().Select(n => n.GetInt32()).ToArray();
        int line = 0, column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            line += data[i];
            if (line == 0 && column == offset) return EditorModel.TokenTypes[data[i + 3]];
        }
        return null;
    }
}
