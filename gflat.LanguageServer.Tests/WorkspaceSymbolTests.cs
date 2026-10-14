using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class WorkspaceSymbolTests
{
    private static EditorModel Analyze(string main, string library = "")
    {
        SourceFile[] sources = [new(Path.GetFullPath("search-main.gf"), main), new(Path.GetFullPath("search-library.gf"), library)];
        return new(new AnalysisSnapshot(sources), sources);
    }

    [Fact]
    public void SearchIncludesUnopenedProjectDeclarationsAndNativeKinds()
    {
        var model = Analyze("int Use() => Lib::Answer();", "namespace Lib; public struct Packet { public int value; public int Length => value; } public int Answer() => 42;");
        var answer = Assert.Single(model.WorkspaceSymbols("answer"));
        Assert.Equal("Lib", answer.ContainerName);
        Assert.EndsWith("search-library.gf", answer.Location.Uri);
        Assert.Equal(6, answer.Kind);
        Assert.Equal(23, Assert.Single(model.WorkspaceSymbols("Packet"), s => s.Name == "Packet").Kind);
        Assert.Equal(8, Assert.Single(model.WorkspaceSymbols("value")).Kind);
        Assert.Equal(7, Assert.Single(model.WorkspaceSymbols("Lib::Packet::Length")).Kind);
    }

    [Fact]
    public void LocalsParametersAndTextAreExcluded()
    {
        var model = Analyze("int Work(int input) { int local = input; /* hidden */ return local; }");
        Assert.Empty(model.WorkspaceSymbols("input"));
        Assert.Empty(model.WorkspaceSymbols("local"));
        Assert.Empty(model.WorkspaceSymbols("hidden"));
        Assert.Single(model.WorkspaceSymbols("Work"));
        Assert.DoesNotContain(model.WorkspaceSymbols(""), s => s.Location.Uri.Contains("Prelude"));
    }

    [Fact]
    public void QueryMatchesQualifiedNamesWithoutLosingContainers()
    {
        var model = Analyze("namespace Outer::Inner { struct Packet { public int Value; } }");
        var field = Assert.Single(model.WorkspaceSymbols(" outer::inner::packet::value "));
        Assert.Equal("Value", field.Name);
        Assert.Equal("Outer::Inner::Packet", field.ContainerName);
        Assert.Empty(model.WorkspaceSymbols("does-not-exist"));
    }

    [Fact]
    public void OverloadsRemainDistinctAndGenericInstancesDoNotDuplicateDeclarations()
    {
        var model = Analyze("int Read(int value) => value; int Read(char value) => 1; T Identity<T>(T value) => value; int Use() => Identity(1); char Other() => Identity('x');");
        var overloads = model.WorkspaceSymbols("Read").Where(s => s.Name == "Read").ToArray();
        Assert.Equal(2, overloads.Length);
        Assert.NotEqual(overloads[0].Location.Range, overloads[1].Location.Range);
        Assert.Single(model.WorkspaceSymbols("Identity"));
        Assert.DoesNotContain(model.WorkspaceSymbols(""), s => s.Name.Contains('$'));
    }

    [Fact]
    public void NamespacedConstructorsKeepSourceNamesAndTypeContainers()
    {
        var model = Analyze("namespace Lib; struct Packet { public Packet(int value) {} }");
        var matches = model.WorkspaceSymbols("Packet");
        Assert.Equal(2, matches.Length);
        var constructor = Assert.Single(matches, s => s.Kind == 9);
        Assert.Equal("Packet", constructor.Name);
        Assert.Equal("Lib::Packet", constructor.ContainerName);
    }

    [Fact]
    public void DestructorsAndOperatorsUseSourceNamesAndLocations()
    {
        var model = Analyze("namespace Lib { struct Packet { public Packet() {} ~Packet() {} public static int operator +(Packet a, Packet b) => 1; } }");
        var destructor = Assert.Single(model.WorkspaceSymbols("~Packet"));
        Assert.Equal(6, destructor.Kind);
        Assert.Equal("Lib::Packet", destructor.ContainerName);
        var op = Assert.Single(model.WorkspaceSymbols("operator +"));
        Assert.Equal(25, op.Kind);
        Assert.Equal("Lib::Packet", op.ContainerName);
        Assert.True(op.Location.Range.End.Character > op.Location.Range.Start.Character);
    }

    [Fact]
    public void RecoverableErrorsDoNotEraseHealthySearchResults()
    {
        var model = Analyze("void Broken() { unknown(); } int Answer() => 42;");
        Assert.Single(model.WorkspaceSymbols("Answer"));
    }
}
