using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class SignatureHelpTypeSeparatorTests
{
    private const string Library = """
        struct Pair<A,B> { public Pair(A first, B second) {} }
        void PairUse(Pair<int,long> value, int next) {}
        int Generic<A,B>(A first, B second) => 0;
        void IntUse(int value, int next) {}
        void BoolUse(bool value, int next) {}
        const int Count(int first, int second) => first + second;
        """;

    [Theory]
    [InlineData("PairUse(new Pair<int,long>(1,2l), |5);", "PairUse")]
    [InlineData("IntUse(Generic<int,long>(1,2l), |5);", "IntUse")]
    [InlineData("IntUse(Generic<Pair<int,long>,int>(new Pair<int,long>(1,2l),3), |5);", "IntUse")]
    [InlineData("IntUse(Generic<int,Pair<int,long>>(1,new Pair<int,long>(2,3l)), |5);", "IntUse")]
    [InlineData("BoolUse(1 < 2, |5);", "BoolUse")]
    [InlineData("BoolUse(1 < 2 && 3 > 2, |5);", "BoolUse")]
    [InlineData("int[Count(1, |2)] storage;", "Count")]
    public void OnlyExpressionArgumentSeparatorsAdvanceTheActiveParameter(string statement, string method)
    {
        string text = Library + "int main(){" + statement + "return 0;}";
        int offset = text.IndexOf('|');
        text = text.Remove(offset, 1);
        var source = new SourceFile(Path.GetFullPath("signature-type-separators.gf"), text);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, [source]);
        var help = JsonSerializer.SerializeToElement(model.SignatureHelp(source.Path, offset), Protocol.JsonOptions);
        Assert.Equal(1, help.GetProperty("activeParameter").GetInt32());
        Assert.Contains(method + "(", help.GetProperty("signatures")[0].GetProperty("label").GetString());
    }
}
