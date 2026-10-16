using gflat.diagnostics;

namespace gflat.Tests;

public sealed class TypeNameDiagnosticTests
{
    [Theory]
    [InlineData(
        "struct Nest<T> { public struct Inner {} } int main() { Nest<int>::Inner<int> value; return 0; }",
        "Unknown generic type 'Nest<int>::Inner'")]
    [InlineData(
        "struct Box<T> { public T value; } int main() { Box<int> item = new Box<int>(); item.nope; return 0; }",
        "'Box<int>' has no field or method 'nope'")]
    [InlineData(
        "struct Box<T> { public Box(T input) {} } int main() { Box<int> item = new Box<int>(\"bad\"); return 0; }",
        "No matching constructor found for 'Box<int>' with arguments (readonly(char)*)")]
    [InlineData(
        "namespace App { class Player {} } int main() { App::Player item = new App::Player(); item.nope; return 0; }",
        "Class 'App::Player' has no field or method 'nope'")]
    [InlineData(
        "namespace Data { static interface IProvider { int Get(); } } void F(Data::IProvider* ptr) {}",
        "Static interface 'Data::IProvider' is a type constraint, not an instance reference")]
    [InlineData(
        "namespace App { class Box<T> { public Box(T input) {} } } int main() { App::Box<int> item = new App::Box<int>(\"bad\"); return 0; }",
        "No matching constructor found for 'App::Box<int>' with arguments (readonly(char)*)")]
    [InlineData(
        "struct Source<T> {} struct Target<T> { public Target(int value) {} } int main() { Target<int> item = new Target<int>(new Source<int>()); return 0; }",
        "No matching constructor found for 'Target<int>' with arguments (Source<int>)")]
    [InlineData(
        "struct Source<T> {} class Target<T> { public Target(int value) {} } int main() { Target<int> item = new Target<int>(new Source<int>()); return 0; }",
        "No matching constructor found for 'Target<int>' with arguments (Source<int>)")]
    public void SourceDiagnosticsUseReadableTypeNamesWithoutEditorPostprocessing(string source, string expected)
    {
        var diagnostics = Compiler.Analyze([new SourceFile("type-name.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("type-name.gf", error.FilePath);
        Assert.Equal(expected, error.Message);
    }
}
