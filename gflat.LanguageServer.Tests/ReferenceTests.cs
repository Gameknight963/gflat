using gflat.LanguageServer;

namespace gflat.LanguageServer.Tests;

public sealed class ReferenceTests
{
    private static (EditorModel Model, SourceFile Source, int Offset) Analyze(string marked, string extra = "")
    {
        int offset = marked.IndexOf('|');
        var source = new SourceFile(Path.GetFullPath("references-main.gf"), marked.Remove(offset, 1));
        var library = new SourceFile(Path.GetFullPath("references-library.gf"), extra);
        return (new EditorModel(new AnalysisSnapshot([source, library]), [source, library]), source, offset);
    }

    [Fact]
    public void LocalReferencesExcludeOtherScopesAndText()
    {
        var (model, source, offset) = Analyze("int F() { int |value = 1; value++; /* value */ return value; } int G() { int value = 2; return value; }");
        var reads = model.References(source.Path, offset, false);
        Assert.Equal(2, reads.Length);
        Assert.All(reads, location => Assert.Equal(Workspace.FileUri(source.Path), location.Uri));
        Assert.Equal(3, model.References(source.Path, offset, true).Length);
    }

    [Fact]
    public void CrossFileCallsAndDeclarationShareOneSymbol()
    {
        var (model, source, offset) = Analyze("int F() => |Answer(); int G() => Answer();", "int Answer() => 42;");
        Assert.Equal(2, model.References(source.Path, offset, false).Length);
        var all = model.References(source.Path, offset, true);
        Assert.Equal(3, all.Length);
        Assert.Contains(all, location => location.Uri.EndsWith("references-library.gf"));
        Assert.Equal(all, model.References(source.Path, offset, true));
    }

    [Fact]
    public void OverloadsHaveDistinctReferenceSets()
    {
        var (model, source, offset) = Analyze("int Read(int n) => n; int Read(char n) => 1; int F() => |Read(1); int G() => Read('x');");
        Assert.Single(model.References(source.Path, offset, false));
        Assert.Equal(2, model.References(source.Path, offset, true).Length);
    }

    [Fact]
    public void BoundAndOrdinaryExtensionCallsShareTheirDeclaration()
    {
        var (model, source, offset) = Analyze("using Extras; int F() { int n = 2; return n.|Double() + Extras::Double(n); }",
            "namespace Extras; int Double(this int n) => n * 2;");
        Assert.Equal(2, model.References(source.Path, offset, false).Length);
        Assert.Equal(3, model.References(source.Path, offset, true).Length);
    }

    [Fact]
    public void AliasReferencesPreserveAliasIdentity()
    {
        var (model, source, offset) = Analyze("alias |Handle = void*; Handle F(Handle value) => value; void* G(void* value) => value;");
        Assert.Equal(2, model.References(source.Path, offset, false).Length);
    }

    [Fact]
    public void FieldsAndPropertiesDoNotMatchUnrelatedMembers()
    {
        var (model, source, offset) = Analyze("struct A { public int |value; public int P => value; } struct B { public int value; } int F() { A a = new A(); B b = new B(); return a.value + b.value; }");
        Assert.Equal(2, model.References(source.Path, offset, false).Length);
    }

    [Fact]
    public void GenericSpecializationsReferToTheirSourceDefinition()
    {
        var (model, source, offset) = Analyze("T |Identity<T>(T value) => value; int F() => Identity(1); char G() => Identity('x');");
        Assert.Equal(2, model.References(source.Path, offset, false).Length);
    }

    [Fact]
    public void RecoverableErrorsDoNotEraseLaterReferences()
    {
        var (model, source, offset) = Analyze("void Broken() { missing(); } int |Answer() => 42; int F() => Answer();");
        Assert.Single(model.References(source.Path, offset, false));
    }

    [Theory]
    [InlineData("int F() => |missing;")]
    [InlineData("int F() => |42;")]
    public void UnresolvedOrNonSymbolPositionsReturnAnEmptyArray(string text)
    {
        var (model, source, offset) = Analyze(text);
        Assert.Empty(model.References(source.Path, offset, true));
    }
}
