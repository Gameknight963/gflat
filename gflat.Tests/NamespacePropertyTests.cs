using gflat.CompileExceptions;

namespace gflat.Tests;

public class NamespacePropertyTests
{
    [Fact]
    public void ExplicitAccessorsSupportQualifiedReadsWritesAndUpdates()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char*) text, ...);
            namespace Settings {
                public int Value { get => 4; set { printf("%d", value); } }
                int Read() => Value;
            }
            int main() {
                int assigned = (Settings::Value = 3);
                Settings::Value += 2;
                int old = Settings::Value++;
                return assigned + old + Settings::Read();
            }
            """);
        Assert.Equal(11, result.ExitCode);
        Assert.Equal("365", result.StandardOutput);
    }

    [Fact]
    public void GlobalNestedAndImportedPropertiesWorkAcrossFiles()
    {
        Assert.Equal(57, CompilerTestHelper.Run([
            new SourceFile("main.gf", "using Library::Nested; int Base => 10; int Imported() => Answer; int main() { int Answer = 2; return global::Base + Library::Nested::Answer + Answer + Read() + Imported(); }"),
            new SourceFile("library.gf", "using Helpers; namespace Library::Nested; public int Answer => getAnswer(); public int Read() => Answer;"),
            new SourceFile("helper.gf", "namespace Helpers; int getAnswer() => 15;")
        ]).ExitCode);
    }

    [Fact]
    public void ThrowingAccessorsPropagateNormally()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(readonly(char*) text, ...);
            namespace Settings {
                int Value { get { throw new Exception(); } set { throw new Exception(); } }
            }
            int main() {
                try { int n = Settings::Value; } catch { printf("G"); }
                try { Settings::Value = 1; } catch { printf("S"); }
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("GS", result.StandardOutput);
    }

    [Theory]
    [InlineData("int P { get => 1; set; }", "either have bodies")]
    [InlineData("int P { get => 1; } = 2;", "initializers")]
    [InlineData("readonly int P => 1;", "receiver modifiers")]
    [InlineData("int P { readonly get => 1; }", "readonly")]
    [InlineData("int P { get => this.value; }", "not a struct")]
    [InlineData("int P => 1; int main() { P = 2; return 0; }", "no setter")]
    [InlineData("int P { set {} } int main() => P;", "no getter")]
    [InlineData("int P => 1; int main() { int* p = &P; return 0; }", "address of a property")]
    [InlineData("int P => 1; int P => 2;", "Duplicate declaration")]
    [InlineData("using A; using B; namespace A { int P => 1; } namespace B { int P => 2; } int main() => P;", "ambiguous")]
    public void InvalidNamespacePropertiesAreDiagnosed(string source, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check(source));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void RestrictedAccessorsAreFilePrivate()
    {
        var files = new[] {
            new SourceFile("library.gf", "namespace Library; public int P { get => 1; private set {} } void Set() { P = 2; }"),
            new SourceFile("main.gf", "int main() { Library::P = 3; return 0; }")
        };
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(files));
        Assert.Contains("private setter", error.Message);
    }
}
