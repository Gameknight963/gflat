using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StaticClassTests
{
    [Fact]
    public void GenericStaticMethodsKeepTheirOwnersAcrossSpecializationAndReuse()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            namespace Providers {
                public static class First {
                    private static int Offset() => 10;
                    public static int Apply<T>(T value) => Offset();
                }
                public static class Second {
                    private static int Offset() => 11;
                    public static int Apply<T>(T value) => Offset();
                }
            }
            int Read() => Providers::First::Apply<int>(1) + Providers::Second::Apply<int>(1);
            namespace Caller {
                public int Read() => Providers::Second::Apply<int>(1) + Providers::First::Apply<int>(1);
            }
            int main() => Read() + Caller::Read();
            """).ExitCode);
    }

    [Fact]
    public void ClosedGenericScopeCanBeAliasedOnce()
    {
        var result = CompilerTestHelper.Run("""
            static class Values<T> {
                public static T Identity(T value) => value;
                public struct Box { public T value; }
            }
            alias IntValues = Values<int>;
            int main() {
                IntValues::Box box = new IntValues::Box();
                box.value = IntValues::Identity(42);
                return box.value;
            }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void StaticScopeCanBeUsedAcrossFilesWithoutImportingItsMembers()
    {
        var result = CompilerTestHelper.Run([
            new SourceFile("library.gf", """
                namespace Library;
                public static class Numbers {
                    public static int Answer => 42;
                    public static int Add(int a, int b) => a + b;
                    public struct Item { public int value; }
                    public static class Nested { public static int Get() => 2; }
                }
                """),
            new SourceFile("main.gf", """
                int main() {
                    Library::Numbers::Item item = new Library::Numbers::Item();
                    item.value = Library::Numbers::Answer;
                    return Library::Numbers::Add(item.value, Library::Numbers::Nested::Get()) - 2;
                }
                """)
        ]);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void StaticClassDoesNotEmitObjectLayoutOrLifetimeFunctions()
    {
        var ir = Compiler.Emit("static class Utility { public static int Get() => 42; } int main() => Utility::Get();");
        Assert.DoesNotContain("%Utility = type", ir);
        Assert.DoesNotContain("@Utility$vtable", ir);
        Assert.DoesNotContain("Utility$ctor", ir);
        Assert.DoesNotContain("Utility$dtor", ir);
    }

    [Theory]
    [InlineData("abstract static class S {}", "cannot be abstract")]
    [InlineData("static class S { int field; }", "must be static")]
    [InlineData("static class S { int F() => 1; }", "must be static")]
    [InlineData("static class S { int Value => 1; }", "must be static")]
    [InlineData("static class S { public S() {} }", "constructors")]
    [InlineData("static class S { ~S() {} }", "destructors")]
    [InlineData("class B {} static class S : B {}", "base types")]
    [InlineData("static class S {} class D : S {}", "inherit from static")]
    [InlineData("static class S {} void F() { S s; }", "instance type")]
    [InlineData("static class S {} void F(S* s) {}", "instance type")]
    [InlineData("static class S {} void F() { new S(); }", "construct static")]
    [InlineData("static class S {} void F() { new* S(); }", "construct static")]
    [InlineData("static class S {} static class S {}", "Duplicate declaration")]
    [InlineData("class Outer { static struct S {} }", "Only classes")]
    public void InvalidStaticClassesHaveSourceDiagnostics(string source, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit([new SourceFile("static.gf", source)]));
        Assert.Contains(message, error.Message);
        Assert.Equal("static.gf", error.Span.Source!.Path);
    }
}
