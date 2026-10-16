using gflat.diagnostics;

namespace gflat.Tests;

public sealed class TypeNameDiagnosticTests
{
    [Theory]
    [InlineData(
        "namespace App { interface IItem {} } void F(App::IItem value) {}",
        "Cannot use interface 'App::IItem' as a value type. Interfaces must be used as pointers ('App::IItem*')")]
    [InlineData(
        "namespace App { static class Tools {} } int main() { new App::Tools(); return 0; }",
        "Cannot construct static class 'App::Tools'")]
    [InlineData(
        "namespace App { abstract class Base {} } int main() { new App::Base(); return 0; }",
        "Cannot instantiate abstract class 'App::Base'")]
    [InlineData(
        "namespace App { class Box<T> {} } int main() { new App::Box<int>(1); return 0; }",
        "Class 'App::Box<int>' does not define a constructor taking 1 arguments")]
    [InlineData(
        "namespace App { struct Box<T> {} } int main() { new App::Box<int>(1); return 0; }",
        "Struct 'App::Box<int>' does not define a constructor taking 1 arguments")]
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
