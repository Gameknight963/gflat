using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class BigIntegerModPowEditorTests
{
    [Fact]
    public void BorrowedExponentAndModulusReturnAnOwningBigIntegerAndNavigateActualMethod()
    {
        const string text = """
            using std; using std::Numerics;
            void Observe(immutable(BigInteger)* value, immutable(BigInteger)* exponent, immutable(BigInteger)* modulus) {
                BigInteger result = value.ModPow(exponent, modulus);
            }
            """;
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        SourceFile library = files.Single(file => Path.GetFileName(file.Path) == "BigInteger.gf");
        var source = new SourceFile(Path.GetFullPath("bigint-modpow-editor.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);
        int use = text.IndexOf("ModPow(", StringComparison.Ordinal);
        Assert.True(use >= 0);
        Assert.Single(Json(model.Completion(source.Path, use)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "ModPow");
        string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("immutable BigInteger", hover);
        Assert.Contains("immutable(BigInteger)* exponent", hover);
        Assert.Contains("immutable(BigInteger)* modulus", hover);
        Assert.DoesNotContain("nothrow", hover);
        Assert.DoesNotContain('$', hover);
        const string prefix = "public immutable BigInteger ";
        int declaration = library.Text.IndexOf(prefix + "ModPow(", StringComparison.Ordinal);
        Assert.True(declaration >= 0);
        var expected = model.Definition(library.Path, declaration + prefix.Length);
        Assert.NotNull(expected);
        Assert.Equal(expected, model.Definition(source.Path, use));
        int comma = text.IndexOf(',', use);
        Assert.True(comma > use);
        var help = Json(model.SignatureHelp(source.Path, comma + 1));
        Assert.Equal(1, help.GetProperty("activeParameter").GetInt32());
        var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
        Assert.Equal(2, signature.GetProperty("parameters").GetArrayLength());
        Assert.Contains("std::Numerics::BigInteger", signature.GetProperty("label").GetString());
    }

    private static JsonElement Json(object? value)
        => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
