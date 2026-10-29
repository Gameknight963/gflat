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
    [InlineData(
        "namespace Lib { class Base { public Base(int value) {} } class Derived : Base { public Derived() : base(\"hello\") {} } } int main()=>0;",
        "No matching base constructor found for 'Lib::Base' with arguments (readonly(char)*)")]
    [InlineData(
        "namespace Lib { class Base<T> { public Base(int value) {} } class Derived : Base<int> { public Derived() : base(\"hello\") {} } } int main()=>0;",
        "No matching base constructor found for 'Lib::Base<int>' with arguments (readonly(char)*)")]
    [InlineData(
        "namespace Lib { class Base { public Base(int value) {} } class Derived : Base { public Derived() {} } } int main()=>0;",
        "Class 'Lib::Derived' must explicitly call a base constructor because base class 'Lib::Base' does not define a parameterless constructor")]
    [InlineData(
        "namespace Lib { class Base<T> { public Base(int value) {} } class Derived<U> : Base<int> { public Derived() {} } } int main(){new Lib::Derived<long>();return 0;}",
        "Class 'Lib::Derived<long>' must explicitly call a base constructor because base class 'Lib::Base<int>' does not define a parameterless constructor")]
    [InlineData(
        "namespace Lib { class Only { public Only() : base() {} } } int main()=>0;",
        "Cannot call base constructor because 'Lib::Only' does not inherit from a base class")]
    [InlineData(
        "namespace Lib { class Only<T> { public Only() : base() {} } } int main(){new Lib::Only<int>();return 0;}",
        "Cannot call base constructor because 'Lib::Only<int>' does not inherit from a base class")]
    [InlineData(
        "namespace Lib { struct Payload<T> {} class Base { public Base(int value) {} } class Derived : Base { public Derived() : base(new Payload<int>()) {} } } int main()=>0;",
        "No matching base constructor found for 'Lib::Base' with arguments (Lib::Payload<int>)")]
    [InlineData(
        "namespace Lib { struct Item {} } int main(){Lib::Item[1] values;foreach(int value in values){}return 0;}",
        "Cannot assign array element type 'Lib::Item' to foreach variable of type 'int'")]
    [InlineData(
        "namespace Lib { struct Item<T> {} } int main(){int[1] values;foreach(Lib::Item<int> value in values){}return 0;}",
        "Cannot assign array element type 'int' to foreach variable of type 'Lib::Item<int>'")]
    [InlineData(
        "interface ITextWriter { void Write(readonly(char)* data, nuint count); }\nstruct Destination : ITextWriter {\n    public static Destination operator t\"\"(readonly(char)* text, nuint count) => new Destination();\n    public void Write(readonly(char)* text, nuint count) {}\n}\r\nnamespace Lib { struct Item {} }\nint main() { Lib::Item value; t$\"{value}\"; return 0; }",
        "Interpolation value of type 'Lib::Item' must implement IFormattable or IStringConvertable; raw pointers are not implicitly treated as text")]
    [InlineData(
        "interface ITextWriter { void Write(readonly(char)* data, nuint count); }\nstruct Destination : ITextWriter {\n    public static Destination operator t\"\"(readonly(char)* text, nuint count) => new Destination();\n    public void Write(readonly(char)* text, nuint count) {}\n}\r\nnamespace Lib { struct Box<T> {} }\nint main() { Lib::Box<int> value; t$\"{value}\"; return 0; }",
        "Interpolation value of type 'Lib::Box<int>' must implement IFormattable or IStringConvertable; raw pointers are not implicitly treated as text")]
    [InlineData(
        "interface ITextWriter { void Write(readonly(char)* data, nuint count); }\nstruct Destination : ITextWriter {\n    public static Destination operator t\"\"(readonly(char)* text, nuint count) => new Destination();\n    public void Write(readonly(char)* text, nuint count) {}\n}\r\nnamespace Lib { struct Item {} }\nint main() { Lib::Item value; t$\"{&value}\"; return 0; }",
        "Interpolation value of type 'Lib::Item*' must implement IFormattable or IStringConvertable; raw pointers are not implicitly treated as text")]
    public void SourceDiagnosticsUseReadableTypeNamesWithoutEditorPostprocessing(string source, string expected)
    {
        var diagnostics = Compiler.Analyze([new SourceFile("type-name.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("type-name.gf", error.FilePath);
        Assert.Equal(expected, error.Message);
    }

    [Theory]
    [InlineData(
        "namespace Lib { struct Item {} } int main(){Lib::Item value;1.0f+value;return 0;}",
        "Operator '+' cannot be applied to operands of type 'float' and 'Lib::Item'")]
    [InlineData(
        "namespace Lib { struct Box<T> {} } int main(){Lib::Box<int> value;value+1.0d;return 0;}",
        "Operator '+' cannot be applied to operands of type 'Lib::Box<int>' and 'double'")]
    [InlineData(
        "namespace Lib { struct Item {} } int main(){Lib::Item value;(&value)+1.0f;return 0;}",
        "Operator '+' cannot be applied to operands of type 'Lib::Item*' and 'float'")]
    public void FloatingOperatorDiagnosticsUseSourceTypeNames(string source, string expected)
    {
        var diagnostics = Compiler.Analyze([new SourceFile("floating-type-name.gf", source)], new() { NoStdLib = true });
        var error = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("GF1002", error.Descriptor.Id);
        Assert.Equal(expected, error.Message);
        Assert.DoesNotContain("$", error.Message);
    }
}
