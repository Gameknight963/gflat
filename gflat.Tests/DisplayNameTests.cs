using gflat.ast;
using gflat.diagnostics;

namespace gflat.Tests;

public class DisplayNameTests
{
    [Fact]
    public void NamespacedGenericAndNestedTypesKeepSourceSpelling()
    {
        var source = new SourceFile(Path.GetFullPath("names.gf"), "namespace std { class Outer { public class Inner {} } struct Box<T> { public T value; } Box<Outer::Inner*> Read() => 1; }");
        var snapshot = new AnalysisSnapshot([source]);
        var error = Assert.Single(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        string message = snapshot.Checker!.DisplayDiagnostic(error.Message);
        Assert.Contains("std::Box<std::Outer::Inner*>", message);
        Assert.DoesNotContain("$", message);
    }

    [Fact]
    public void NestedAndGenericTypesInDiagnosticsUseSourceNames()
    {
        var source = new SourceFile(Path.GetFullPath("names.gf"), "class Outer { public class Inner {} } struct Box<T> { public T value; } Box<Outer::Inner*> Read() => 1;");
        var snapshot = new AnalysisSnapshot([source]);
        var error = Assert.Single(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        string message = snapshot.Checker!.DisplayDiagnostic(error.Message);
        Assert.Contains("Box<Outer::Inner*>", message);
        Assert.DoesNotContain("$", message);
        Assert.DoesNotContain("Outer.Inner", message);
    }

    [Fact]
    public void DisplayFormattingDoesNotChangeInternalTypeIdentity()
    {
        var source = new SourceFile(Path.GetFullPath("names.gf"), "class Outer { public class Inner {} } Outer::Inner* Read(Outer::Inner* value) => value;");
        var snapshot = new AnalysisSnapshot([source]);
        Assert.DoesNotContain(snapshot.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var method = snapshot.Nodes.Select(n => n.Node).OfType<MethodDeclaration>().Single(m => m.Name == "Read");
        string identity = TypeChecker.TypeName(method.ReturnType);
        Assert.Equal("Outer::Inner*", snapshot.Checker!.DisplayTypeName(method.ReturnType));
        Assert.Equal(identity, TypeChecker.TypeName(method.ReturnType));
    }
}
