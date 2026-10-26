using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class GenericDeclarationContextTests
{
    [Fact]
    public void CallerLocalCannotShadowSpecializedIteratorField()
    {
        var result = CompilerTestHelper.Run([
            new SourceFile("core/Collections/IEnumerable.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Collections/IEnumerable.gf"))),
            new SourceFile("core/Queries/Enumerable.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Queries/Enumerable.gf"))),
            new SourceFile("Span.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Memory/Span.gf"))),
            new SourceFile("main.gf", "using std; using std::Queries; using std::Collections; int main() { int[1] data = [42]; Span<int> view = new Span<int>(data, 1); foreach (int item in view) return item; return 1; }")
        ]);
        Assert.Equal(42, result.ExitCode);
    }

    [Theory]
    [InlineData("T Callee<T>() => (T)missing;", "int missing = 42;")]
    [InlineData("T Callee<T>() { CallerAlias item = 42; return (T)item; }", "alias CallerAlias = int;")]
    [InlineData("T Callee<T>() => (T)CallerEnum::Item;", "enum CallerEnum { Item = 42 }")]
    public void GenericBodyCannotCaptureCallerDeclarations(string declaration, string local)
        => Assert.Throws<TypeCheckException>(() => Compiler.Emit(declaration + " int main() { " + local + " return Callee<int>(); }"));

    [Fact]
    public void GenericBodyHasItsOwnLambdaReturnAndCaptureContext()
    {
        var result = CompilerTestHelper.Run("""
            T Callee<T>(T value) => value;
            int main() { bool(int)* project = (int value) => Callee(value) == 42; if (project(42)) return 42; return 1; }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void GenericReturnIsNotInsideCallersDefer()
    {
        var result = CompilerTestHelper.Run("""
            T Callee<T>(T value) => value;
            int main() { int value = 0; { defer { value = Callee(42); } } return value; }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void GenericBodyCannotBreakCallersLoop()
        => Assert.Throws<TypeCheckException>(() => Compiler.Emit("void Callee<T>(T value) { break; } int main() { while (true) { Callee(1); break; } return 0; }"));

    [Fact]
    public void CallerConstructorPrivilegesAndNullableFactsAreRestored()
    {
        var result = CompilerTestHelper.Run("""
            T Callee<T>(T value) => value;
            struct Owner {
                public readonly(int) value;
                public Owner() { int number = Callee(1); value = 42; }
            }
            struct Item { public int Get() => 42; }
            int Read(Item*? item) { if (item == null) return 1; int number = Callee(1); return item.Get(); }
            int main() { Owner owner = new Owner(); Item item = new Item(); if (owner.value != 42) return 2; return Read(&item); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void CallerConstantDoesNotMakeCalleeLocalReadOnly()
    {
        var result = CompilerTestHelper.Run("""
            T Callee<T>(T value) { int x = 1; x = 2; return value; }
            int main() { const int x = 42; return Callee(x); }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void CalleeConstantDoesNotPoisonCallerOrLaterLocalBindings()
    {
        var result = CompilerTestHelper.Run("""
            T Callee<T>(T value) { const int x = 1; return value; }
            int Later() { int x = 41; x++; return x; }
            int main() { int value = Callee(0); int x = 41; x++; return x + Later() - 42; }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void GlobalConstantsRemainDistinctFromShadowingLocals()
    {
        var result = CompilerTestHelper.Run("""
            const int x = 21;
            T Callee<T>() => (T)x;
            int Mutable() { int x = 40; x += 2; return x; }
            int main() { const int x = 42; if (Callee<int>() != 21 || Mutable() != 42) return 1; return x; }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void LocalConstantsRestoreEnclosingBindingAndArraysHaveStaticAddresses()
    {
        var result = CompilerTestHelper.Run("""
            immutable(char*) Text() { const char[3] text = ['o', 'k', '\0']; return &text[0]; }
            int main() {
                int x = 41; { const int x = 99; if (x != 99) return 1; }
                x++; if (Text()[0] != 'o') return 2; return x;
            }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void LocalEnumsKeepTheirOwnIdentityAndDoNotEscape()
    {
        var result = CompilerTestHelper.Run("""
            extern int printf(immutable(char*) text, ...);
            int First() { enum Color { Red = 1 } Color color = Color::Red; printf("%s ", typeof(Color).Name); return (int)color; }
            int main() { enum Color { Red = 41 } Color color = Color::Red; printf("%s", typeof(Color).Name); return First() + (int)color; }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("ColorColor ", result.StandardOutput);
        Assert.Throws<TypeCheckException>(() => Compiler.Emit("int First() { enum Color { Red = 1 } return 0; } int main() { Color color; return 0; }"));
    }

    [Fact]
    public void ConstantInterpreterUsesRuntimeFramesThenBoundGlobalDeclarations()
    {
        var result = CompilerTestHelper.Run("""
            const int x = 21;
            const int Global() => x;
            const int Next(int x) => x + 1;
            int main() { const int x = 42; const int result = Global(); const int parameter = Next(41); if (result != 21) return 1; return parameter; }
            """);
        Assert.Equal(42, result.ExitCode);
    }

    [Fact]
    public void ForwardConstantInitializerDoesNotInvalidateCallerNullableGuard()
    {
        var result = CompilerTestHelper.Run("""
            struct Item { public int Get() => 42; }
            const int Value() => 1;
            int Read(ref Item*? item) {
                if (item == null) return 1;
                int number = Late;
                return item.Get();
            }
            const int Late = Value();
            int main() { Item value = new Item(); Item*? pointer = &value; return Read(ref pointer); }
            """);
        Assert.Equal(42, result.ExitCode);
    }
}
