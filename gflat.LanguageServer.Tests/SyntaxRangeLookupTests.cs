using System.Reflection;

namespace gflat.LanguageServer.Tests;

public sealed class SyntaxRangeLookupTests
{
    [Theory]
    [InlineData("int Sum(int x, int y) => x + y;")]
    [InlineData("namespace A { struct Box { public int Value; public int Read(int value) { int result = value; { int nested = result; } return result; } } }")]
    [InlineData("int Run() { int value = 0; for (int index = 0; index < 4; index++) { value += index; } return value; }")]
    [InlineData("struct Box<T> { public T Value; } int Read(Box<int>* box) => box.Value;")]
    public void IndexedContextMatchesNarrowestCapturedSpanAtEveryPosition(string text)
    {
        var file = new SourceFile(Path.GetFullPath("syntax-ranges.gf"), text);
        var snapshot = new AnalysisSnapshot([file], new() { NoStdLib = true });
        var model = new EditorModel(snapshot, [file]);
        var context = typeof(EditorModel).GetMethod("Context", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var captured = snapshot.Nodes.Where(node => node.Span.Source?.Id == file.Id).ToArray();
        for (int offset = -1; offset <= text.Length + 1; offset++)
        {
            var expected = captured.Where(node => offset >= node.Span.Start && offset < node.Span.Start + node.Span.Length)
                .MinBy(node => node.Span.Length);
            Assert.Same(expected, context.Invoke(model, [file.Path, offset]));
        }
    }
}
