using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class RenameTests
{
    private static (EditorModel Model, SourceFile Source, int Offset) Analyze(string marked, string extra = "")
    {
        int offset = marked.IndexOf('|');
        var source = new SourceFile(Path.GetFullPath("rename.gf"), marked.Remove(offset, 1));
        var library = new SourceFile(Path.GetFullPath("rename-library.gf"), extra);
        return (new EditorModel(new AnalysisSnapshot([source, library]), [source, library]), source, offset);
    }

    [Theory]
    [InlineData("int F() { int |value = 1; value++; return value; } int G() { int value = 2; return value; }", 3)]
    [InlineData("int F(ref int |value) { value++; return value; }", 3)]
    [InlineData("readonly(char*) F(int |value) => nameof(value);", 2)]
    [InlineData("struct S { public S(int |value) { int copy = value; } }", 2)]
    [InlineData("struct S { public int this[int |index] { get => index; } }", 2)]
    [InlineData("int F() { int(int)* f = (int |value) => value; return f(1); }", 2)]
    public void RenamesOnlyTheSelectedLocalOrParameter(string marked, int count)
    {
        var (model, source, offset) = Analyze(marked);
        Assert.NotNull(model.PrepareRename(source.Path, offset));
        var edits = model.Rename(source.Path, offset, "renamed");
        Assert.Equal(count, edits.Length);
        Assert.All(edits, edit => { Assert.Equal(source.Path, edit.Path); Assert.Equal("renamed", edit.NewText); });
    }

    [Theory]
    [InlineData("int F(int |value) { int other = 2; return value + other; }", "other")]
    [InlineData("struct S { public int field; public int F() { int |value = 1; return value + field; } }", "field")]
    [InlineData("const int Global = 2; int F() { int |value = 1; return value + Global; }", "Global")]
    public void CollisionsAndSilentCaptureAreRejected(string marked, string name)
    {
        var (model, source, offset) = Analyze(marked);
        Assert.Throws<InvalidOperationException>(() => model.Rename(source.Path, offset, name));
    }

    [Theory]
    [InlineData("int")]
    [InlineData("two names")]
    [InlineData("value+")]
    [InlineData("")]
    [InlineData(" padded")]
    [InlineData("trailing ")]
    [InlineData("@")]
    [InlineData("\"unfinished")]
    public void InvalidNamesAreRejected(string name)
    {
        var (model, source, offset) = Analyze("int F(int |value) => value;");
        Assert.Throws<InvalidOperationException>(() => model.Rename(source.Path, offset, name));
    }

    [Fact]
    public void ErrorsInAnotherFileRefuseRename()
    {
        var (model, source, offset) = Analyze("int F(int |value) => value;", "int Broken() => missing;");
        var error = Assert.Throws<InvalidOperationException>(() => model.PrepareRename(source.Path, offset));
        Assert.Contains("error-free", error.Message);
    }

    [Fact]
    public void MemberNamesCannotBeRenamed()
    {
        var (model, source, offset) = Analyze("struct S { public int |value; }");
        Assert.Null(model.PrepareRename(source.Path, offset));
    }

    [Fact]
    public void ImplicitSetterParameterCannotBeRenamed()
    {
        var (model, source, offset) = Analyze("struct S { public int P { set { int local = |value; } } }");
        Assert.Null(model.PrepareRename(source.Path, offset));
    }

    [Fact]
    public void ApplyingEditsPreservesCommentsLiteralsAndOtherScopes()
    {
        var (model, source, offset) = Analyze("int F(int |value) { nameof(value); // value\n readonly(char*) text = \"value\"; return value; } int G(int value) => value;");
        var edits = model.Rename(source.Path, offset, "renamed");
        string text = source.Text;
        foreach (var edit in edits.OrderByDescending(edit => edit.Range.Start.Line).ThenByDescending(edit => edit.Range.Start.Character)) {
            int start = Workspace.Offset(source.Text, System.Text.Json.JsonSerializer.SerializeToElement(edit.Range.Start, Protocol.JsonOptions));
            int end = Workspace.Offset(source.Text, System.Text.Json.JsonSerializer.SerializeToElement(edit.Range.End, Protocol.JsonOptions));
            text = text.Remove(start, end - start).Insert(start, edit.NewText);
        }
        Assert.Equal("int F(int renamed) { nameof(renamed); // value\n readonly(char*) text = \"value\"; return renamed; } int G(int value) => value;", text);
        Compiler.Emit(text);
    }
}
