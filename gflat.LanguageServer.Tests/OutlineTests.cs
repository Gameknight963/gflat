using System.Text.Json;
using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class OutlineTests
{
    private static JsonElement Outline(string text, bool hierarchical = true)
    {
        var source = new SourceFile(Path.GetFullPath("outline.gf"), text);
        var model = new EditorModel(new AnalysisSnapshot([source]), [source]);
        return JsonSerializer.SerializeToElement(model.DocumentSymbols(source.Path, hierarchical), Protocol.JsonOptions);
    }

    [Fact]
    public void NamespacesTypesAndMembersFormAHierarchyWithoutLocalsOrAccessors()
    {
        var result = Outline("namespace A { struct S { public int value; public int P { get => value; set { value = value; } } public int F(int n) { int local = n; return local; } } }");
        var ns = Assert.Single(result.EnumerateArray());
        Assert.Equal("A", ns.GetProperty("name").GetString());
        Assert.Equal(3, ns.GetProperty("kind").GetInt32());
        var type = Assert.Single(ns.GetProperty("children").EnumerateArray());
        Assert.Equal("S", type.GetProperty("name").GetString());
        Assert.Equal(23, type.GetProperty("kind").GetInt32());
        Assert.Equal(new[] { "value", "P", "F" }, type.GetProperty("children").EnumerateArray().Select(child => child.GetProperty("name").GetString()));
        Assert.All(type.GetProperty("children").EnumerateArray(), child => Assert.Empty(child.GetProperty("children").EnumerateArray()));
    }

    [Fact]
    public void FlatClientsReceiveSymbolInformationWithContainers()
    {
        var result = Outline("namespace A { struct S { public int value; } }", false);
        Assert.Equal(3, result.GetArrayLength());
        var field = result.EnumerateArray().Single(item => item.GetProperty("name").GetString() == "value");
        Assert.Equal("S", field.GetProperty("containerName").GetString());
        Assert.Equal(8, field.GetProperty("kind").GetInt32());
        Assert.True(field.TryGetProperty("location", out _));
        Assert.False(field.TryGetProperty("children", out _));
    }

    [Fact]
    public void FileScopedNestedNamespaceContainsFollowingDeclarations()
    {
        var result = Outline("namespace A::B; alias Handle = void*; int F() => 1;");
        var ns = Assert.Single(result.EnumerateArray());
        Assert.Equal("A", ns.GetProperty("name").GetString());
        var nested = Assert.Single(ns.GetProperty("children").EnumerateArray());
        Assert.Equal("B", nested.GetProperty("name").GetString());
        Assert.Equal(2, nested.GetProperty("children").GetArrayLength());
        Assert.Equal(1, ns.GetProperty("selectionRange").GetProperty("end").GetProperty("character").GetInt32()
            - ns.GetProperty("selectionRange").GetProperty("start").GetProperty("character").GetInt32());
    }

    [Fact]
    public void ConstructorsDestructorsAndOperatorsHaveNavigableSelections()
    {
        var result = Outline("namespace Nested { struct S { public S() {} ~S() {} public static int operator +(S a, S b) => 1; } }");
        var type = Assert.Single(Assert.Single(result.EnumerateArray()).GetProperty("children").EnumerateArray());
        var members = type.GetProperty("children").EnumerateArray().ToArray();
        Assert.Equal(new[] { "S", "~S", "operator +" }, members.Select(item => item.GetProperty("name").GetString()));
        Assert.All(members, item => Assert.True(item.GetProperty("selectionRange").GetProperty("end").GetProperty("character").GetInt32()
            > item.GetProperty("selectionRange").GetProperty("start").GetProperty("character").GetInt32()));
    }

    [Fact]
    public void TypeErrorsPreserveFollowingOutlineEntries()
    {
        var result = Outline("void Broken() { missing(); } struct Later { public int value; }");
        Assert.Equal(new[] { "Broken", "Later" }, result.EnumerateArray().Select(item => item.GetProperty("name").GetString()));
    }
}
