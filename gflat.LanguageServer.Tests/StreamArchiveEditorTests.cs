using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class StreamArchiveEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamAndArchiveMembersKeepClassificationHoverAndNavigation(bool brokenFile)
    {
        const string text = """
            using Standard::Compression;
            using Standard::IO;
            using Standard::Text;

            void Inspect(ZipArchive* archive, MemoryStream* stream)
            {
                ZipArchiveEntry entry = archive.GetEntry("folder/item.txt");
                String name = entry.FullName;
                stream.Write("x", 1);
                long position = stream.Position;
            }
            """;
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "StandardLibrary"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).ToList();
        if (brokenFile)
            files.Insert(0, new SourceFile(Path.GetFullPath("stream-editor/broken.gf"), "void Broken() { int value = ; }"));
        var source = new SourceFile(Path.GetFullPath("stream-editor/main.gf"), text);
        files.Add(source);
        var snapshot = new AnalysisSnapshot(files, new() { NoStdLib = true });
        if (!brokenFile)
            Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        else
            Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = new EditorModel(snapshot, files);

        Assert.Equal("struct", TokenAt(model, source, text.IndexOf("ZipArchive*", StringComparison.Ordinal)));
        Assert.Equal("struct", TokenAt(model, source, text.IndexOf("MemoryStream*", StringComparison.Ordinal)));
        Assert.Equal("struct", TokenAt(model, source, text.IndexOf("ZipArchiveEntry entry", StringComparison.Ordinal)));
        CheckMember(model, source, "GetEntry(", "method", "ZipArchiveEntry", "ZipArchive.gf");
        CheckMember(model, source, "FullName", "property", "String", "ZipArchiveEntry.gf");
        CheckMember(model, source, "Write(", "method", "void", "MemoryStream.gf");
        CheckMember(model, source, "Position", "property", "long", "MemoryStream.gf");
    }

    private static void CheckMember(EditorModel model, SourceFile source, string text, string classification, string result, string file)
    {
        int offset = source.Text.IndexOf(text, StringComparison.Ordinal);
        Assert.True(offset >= 0, text);
        Assert.Equal(classification, TokenAt(model, source, offset));
        var hover = JsonSerializer.SerializeToElement(model.Hover(source.Path, offset), Protocol.JsonOptions);
        string signature = hover.GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains(result, signature);
        Assert.DoesNotContain('$', signature);
        object? definition = model.Definition(source.Path, offset);
        Assert.NotNull(definition);
        Assert.Contains(file, JsonSerializer.Serialize(definition, Protocol.JsonOptions));
    }

    private static string TokenAt(EditorModel model, SourceFile source, int offset)
    {
        int line = source.Text[..offset].Count(character => character == '\n');
        int column = offset - source.Text.LastIndexOf('\n', offset) - 1;
        int[] data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path), Protocol.JsonOptions)
            .GetProperty("data").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int tokenLine = 0, tokenColumn = 0;
        for (int index = 0; index < data.Length; index += 5)
        {
            tokenLine += data[index];
            tokenColumn = data[index] == 0 ? tokenColumn + data[index + 1] : data[index + 1];
            if (tokenLine == line && column >= tokenColumn && column < tokenColumn + data[index + 2])
                return EditorModel.TokenTypes[data[index + 3]];
        }
        Assert.Fail("Missing semantic token at " + line + ":" + column);
        return "";
    }
}
