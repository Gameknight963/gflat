using System.Text.Json;
using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class StandardLibraryTypingRecoveryTests
{
    [Fact]
    public void MissingParameterNameAndClosingParenDoNotConsumeMethodBodyBrace()
    {
        var source = new SourceFile(Path.GetFullPath("missing-parameter-name.gf"), """
            class Container {
                public int Broken(int { return 1; }
                public int Read(int argument) { int local = argument; return local; }
            }
            int Healthy() { Container item = new Container(); return item.Read(42); }
            """);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Descriptor.Id == "GF0005");
        Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Check(source, options: new() { NoStdLib = true }));
        var container = Assert.Single(snapshot.Nodes.Select(node => node.Node).OfType<ClassDeclaration>());
        Assert.Contains(container.Members, member => member is MethodDeclaration { Name: "Read" });
        var call = Assert.Single(snapshot.Nodes.Select(node => node.Node).OfType<CallExpression>());
        Assert.Equal("int", TypeChecker.TypeName(snapshot.Checker!.GetType(call)));
        Assert.NotNull(snapshot.Checker.GetResolvedCall(call));
        var model = new EditorModel(snapshot, [source]);
        int offset = source.Text.LastIndexOf("Read", StringComparison.Ordinal);
        Assert.Equal("method", Colors(model, source)[offset]);
        Assert.NotNull(model.Hover(source.Path, offset));
        Assert.NotNull(model.Definition(source.Path, offset));
    }

    [Theory]
    [InlineData("return String::From(text, count);", "return String::;")]
    [InlineData("public static String From(readonly(char)* text)", "public static String From(readonly(char)*")]
    [InlineData("String result = new String();", "String result = new ;")]
    [InlineData("IEquatable<String>", "IEquatable<String")]
    public void TypingInsideActualStringPreservesOtherFileEditorServices(string original, string replacement)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "StandardLibrary");
        string stringPath = Path.Combine(directory, "core", "String.gf");
        string text = File.ReadAllText(stringPath);
        int edit = text.IndexOf(original, StringComparison.Ordinal);
        Assert.True(edit >= 0, "The mutation must match the actual String fixture.");
        var edited = new SourceFile(stringPath, text[..edit] + replacement + text[(edit + original.Length)..]);
        var sources = Directory.GetFiles(directory, "*.gf", SearchOption.AllDirectories)
            .Select(path => path == stringPath ? edited : new SourceFile(path, File.ReadAllText(path))).ToList();
        var healthy = new SourceFile(Path.GetFullPath("std-editor/typing-companion.gf"), """
            namespace Audit {
                public class Item {
                    public int Read(int argument) { int local = argument; return local; }
                }
            }
            int Healthy() { Audit::Item item = new Audit::Item(); return item.Read(42); }
            """);
        sources.Add(healthy);
        var snapshot = new AnalysisSnapshot(sources, new() { NoStdLib = true });
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.FilePath == edited.Path);
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.FilePath == healthy.Path);
        var local = Assert.Single(snapshot.Nodes.Where(node => node.Span.SourceId == healthy.Id)
            .Select(node => node.Node).OfType<VariableDeclaration>(), variable => variable.Name == "local");
        Assert.Equal("int", TypeChecker.TypeName(snapshot.Checker!.GetType(local)));
        var call = Assert.Single(snapshot.Nodes.Where(node => node.Span.SourceId == healthy.Id)
            .Select(node => node.Node).OfType<CallExpression>());
        Assert.Equal("int", TypeChecker.TypeName(snapshot.Checker.GetType(call)));
        Assert.NotNull(snapshot.Checker.GetResolvedCall(call));
        var model = new EditorModel(snapshot, sources);
        var colors = Colors(model, healthy);
        foreach (var (offset, classification, signature) in new[] {
            (healthy.Text.LastIndexOf("Item", StringComparison.Ordinal), "class", "Item"),
            (healthy.Text.LastIndexOf("Read", StringComparison.Ordinal), "method", "Read(int argument)"),
            (healthy.Text.LastIndexOf("local", StringComparison.Ordinal), "variable", "int local"),
            (healthy.Text.LastIndexOf("item.Read", StringComparison.Ordinal), "variable", "item")
        })
        {
            Assert.Equal(classification, colors[offset]);
            var hover = JsonSerializer.SerializeToElement(model.Hover(healthy.Path, offset), Protocol.JsonOptions);
            Assert.Contains(signature, hover.GetProperty("contents").GetProperty("value").GetString());
            Assert.NotNull(model.Definition(healthy.Path, offset));
        }
        // Malformed syntax need not resolve, but querying either side of the edit
        // must still be safe, as it is during normal typing in Visual Studio.
        foreach (int offset in new[] { edit, edit + replacement.Length - 1 })
        {
            model.Hover(edited.Path, offset);
            model.Definition(edited.Path, offset);
            model.Completion(edited.Path, offset);
        }
        model.SemanticTokens(edited.Path);
    }

    private static Dictionary<int, string> Colors(EditorModel model, SourceFile source)
    {
        var data = JsonSerializer.SerializeToElement(model.SemanticTokens(source.Path)).GetProperty("data")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var starts = new[] { 0 }.Concat(source.Text.Select((character, index) => (character, index))
            .Where(pair => pair.character == '\n').Select(pair => pair.index + 1)).ToArray();
        var result = new Dictionary<int, string>();
        int line = 0, column = 0;
        for (int i = 0; i < data.Length; i += 5)
        {
            line += data[i];
            column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            result[starts[line] + column] = EditorModel.TokenTypes[data[i + 3]];
        }
        return result;
    }
}
