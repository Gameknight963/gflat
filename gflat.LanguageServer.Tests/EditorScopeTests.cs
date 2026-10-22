using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public class EditorScopeTests
{
    private static (EditorModel Model, string Path, int Offset, string Text) Analyze(string marked)
    {
        int offset = marked.IndexOf('|');
        string text = marked.Remove(offset, 1);
        var source = new SourceFile(Path.GetFullPath("scopes.gf"), text);
        var snapshot = new AnalysisSnapshot([source]);
        return (new EditorModel(snapshot, [source]), source.Path, offset, text);
    }
    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, Protocol.JsonOptions);

    [Fact]
    public void NearestMemberWinsRegardlessOfDeclarationOrder()
    {
        var (model, path, offset, _) = Analyze("class Child : Base { int n; int F() => |n; } class Base { public double n; }");
        Assert.Contains("int Child.n", Json(model.Hover(path, offset)).GetProperty("contents").GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("class Base { protected int field; } class Child : Base { int F() => fi|eld; }")]
    [InlineData("class Base { public int field; } class Child : Base { int F() => fi|eld; }")]
    [InlineData("class Base { internal int field; } int F(Base* b) => b.fi|eld;")]
    public void AccessibleInheritedAndInternalMembersHaveHover(string source)
    {
        var (model, path, offset, _) = Analyze(source);
        Assert.NotNull(model.Hover(path, offset));
        Assert.NotNull(model.Definition(path, offset));
    }

    [Fact]
    public void RestrictedGetterDoesNotHidePublicProperty()
    {
        var (model, path, offset, _) = Analyze("struct S { public int Value { private get => 0; set {} } } void F(S* s) { s.Va|lue = 1; }");
        string text = Json(model.Hover(path, offset)).GetProperty("contents").GetProperty("value").GetString()!;
        Assert.Contains("public int S.Value", text);
        Assert.Contains("private get;", text);
    }

    [Fact]
    public void BasePrivateMembersAreNotSuggestedInDerivedClass()
    {
        var (model, path, offset, _) = Analyze("class Base { private int hidden; protected int visible; } class Child : Base { void F() { | } }");
        var labels = Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("label").GetString()).ToArray();
        Assert.Contains("visible", labels);
        Assert.DoesNotContain("hidden", labels);
    }

    [Theory]
    [InlineData("public int this[nuint index] { immutable get { return (int)in|dex; } set {} }", "index")]
    [InlineData("public int this[nuint index] { get => 0; set { int n = (int)in|dex; } }", "index")]
    [InlineData("public int this[nuint index] => (int)in|dex;", "index")]
    [InlineData("public int this[nuint index] { get => 0; set { int n = va|lue; } }", "value")]
    [InlineData("public int Value { get => 0; set { int n = va|lue; } }", "value")]
    [InlineData("int n; public int Value { get => n; set => n = va|lue; }", "value")]
    public void AccessorParametersHaveHoverCompletionNavigationAndColor(string property, string name)
    {
        var (model, path, offset, text) = Analyze("struct S { " + property + " }");
        var hover = Json(model.Hover(path, offset));
        Assert.Contains(name, hover.GetProperty("contents").GetProperty("value").GetString());
        Assert.NotNull(model.Definition(path, offset));
        var items = Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray();
        Assert.Contains(items, item => item.GetProperty("label").GetString() == name);
        var data = Json(model.SemanticTokens(path)).GetProperty("data").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        int column = 0;
        bool found = false;
        for (int i = 0; i < data.Length; i += 5)
        {
            column += data[i + 1];
            if (column <= offset && column + data[i + 2] > offset)
            {
                Assert.Equal("parameter", EditorModel.TokenTypes[data[i + 3]]);
                found = true;
            }
        }
        Assert.True(found);
    }

    [Theory]
    [InlineData("struct S { public int this[int index] { get { |return index; } set {} } }")]
    [InlineData("struct S { public int Value { get => 0; set {} } void F() { | } }")]
    public void SetterValueDoesNotLeakIntoOtherScopes(string source)
    {
        var (model, path, offset, _) = Analyze(source);
        Assert.DoesNotContain(Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "value");
    }

    [Theory]
    [InlineData("using N; namespace N { public struct S { public int Length; } } void F(S* s) { s.| }")]
    [InlineData("namespace N { public struct S { public int Length; } } void F(N::S* s) { s.| }")]
    [InlineData("namespace N { public struct S<T> { public int Length; } } void F(N::S<int>* s) { s.| }")]
    [InlineData("using N; namespace N { public struct S<T> { public int Length; } } void F(S<int>* s) { s.| }")]
    [InlineData("struct S { public int Length; } alias View = S*; void F(View s) { s.| }")]
    public void MemberCompletionResolvesQualifiedGenericAndAliasReceivers(string source)
    {
        var (model, path, offset, _) = Analyze(source);
        Assert.Contains(Json(model.Completion(path, offset)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("label").GetString() == "Length");
    }
}
