using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class PlatformSystemClockEditorTests
{
    [Fact]
    public void ExplicitPlatformWallClocksReturnCivilDateTimeWithSourceNavigation()
    {
        foreach (string platform in new[] { "Windows", "Linux" })
        {
            string providerDirectory = Path.Combine(AppContext.BaseDirectory, "PlatformLibrary", platform.ToLowerInvariant());
            var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(providerDirectory, "*.gf"))
                .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
            var provider = files.Single(file => Path.GetFileName(file.Path) == "SystemClock.gf");
            string text = $"void Probe(){{std::Time::DateTime utc={platform}::SystemClock::GetUtcNow();long ticks=utc.Ticks;}}";
            var source = new SourceFile(Path.GetFullPath("system-clock-editor.gf"), text);
            files.Add(source);
            var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
            Assert.DoesNotContain(snapshot.Diagnostics, error => error.Severity == DiagnosticSeverity.Error);
            var model = new EditorModel(snapshot, files);
            int use = text.IndexOf("GetUtcNow", StringComparison.Ordinal);
            string hover = Json(model.Hover(source.Path, use)).GetProperty("contents").GetProperty("value").GetString()!;
            Assert.Contains("DateTime", hover);
            Assert.Contains("GetUtcNow", hover);
            Assert.DoesNotContain('$', hover);
            int declaration = provider.Text.IndexOf("GetUtcNow", StringComparison.Ordinal);
            var expected = model.Definition(provider.Path, declaration);
            Assert.NotNull(expected);
            Assert.Equal(expected, model.Definition(source.Path, use));
            var help = Json(model.SignatureHelp(source.Path, use + "GetUtcNow(".Length));
            var signature = Assert.Single(help.GetProperty("signatures").EnumerateArray());
            Assert.Equal(0, signature.GetProperty("parameters").GetArrayLength());
        }
    }

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);
}
