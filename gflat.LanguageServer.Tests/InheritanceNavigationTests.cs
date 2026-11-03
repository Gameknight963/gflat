using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer.Tests;

public sealed class InheritanceNavigationTests
{
    private const string Code = """
        interface IValue { int Read(int argument); }
        class Base : IValue { public virtual int Read(int argument) => argument; public virtual int Peek() => 1; }
        class Derived : Base { public override int Read(int argument) => argument + 1; }
        class Leaf : Derived { public override int Read(int argument) => argument + 2; }
        struct Other : IValue { public int Read(int argument) => argument; }
        """;

    private static (EditorModel Model, SourceFile Source) Analyze()
    {
        var source = new SourceFile(Path.GetFullPath("inheritance-navigation.gf"), Code);
        var snapshot = new AnalysisSnapshot([source], new() { NoStdLib = true });
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return (new(snapshot, [source]), source);
    }

    [Fact]
    public void OverrideKeywordNavigatesToNearestBaseMemberAndDistinguishesOverloads()
    {
        var (model, source) = Analyze();
        foreach (int at in new[] { Code.IndexOf("override", StringComparison.Ordinal), Code.LastIndexOf("override", StringComparison.Ordinal) })
        {
            var location = Assert.IsType<Location>(model.Definition(source.Path, at));
            int expected = at == Code.IndexOf("override", StringComparison.Ordinal)
                ? Code.IndexOf("Read(int argument) =>", StringComparison.Ordinal)
                : Code.IndexOf("Read(int argument) => argument + 1", StringComparison.Ordinal);
            Assert.Equal(Workspace.ToRange(source.Span(expected, 4)), location.Range);
        }
    }

    [Fact]
    public void InterfaceImplementationsIncludeStructsAndOverrides()
    {
        var (model, source) = Analyze();
        var implementations = model.Implementations(source.Path, Code.IndexOf("Read", StringComparison.Ordinal));
        Assert.Equal(4, implementations.Length);
        var overload = model.Implementations(source.Path, Code.IndexOf("Peek()", StringComparison.Ordinal));
        Assert.Single(overload);
    }

    [Fact]
    public void HierarchySeparatesDirectBasesAndDirectSubtypes()
    {
        var (model, source) = Analyze();
        int at = Code.IndexOf("class Derived", StringComparison.Ordinal) + 6;
        var prepared = Assert.Single(model.PrepareTypeHierarchy(source.Path, at));
        Assert.Equal("Derived", prepared.Name);
        Assert.Equal("Base", Assert.Single(model.TypeHierarchy(prepared.Data.Path, prepared.Data.Offset, false)).Name);
        Assert.Equal("Leaf", Assert.Single(model.TypeHierarchy(prepared.Data.Path, prepared.Data.Offset, true)).Name);
    }
}
