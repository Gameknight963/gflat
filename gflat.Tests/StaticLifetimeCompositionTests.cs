using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StaticLifetimeCompositionTests
{
    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void OwningStaticFieldsAreUnaffectedByInstanceCopyAndDestruction(string kind)
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            struct Token {
                private int id;
                public Token(int value) { id = value; }
                ~Token() { putchar(id); }
            }
            """ + kind + """
             Holder {
                public static Token shared = new Token(65);
                public int value;
                public Holder(int number) { value = number; }
            }
            int main() {
                Holder first = new Holder(42);
                Holder second = first;
                return second.value - 42;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("A", result.StandardOutput);
    }

    [Fact]
    public void StaticConstructorCanReplaceAnOwningReadonlyAggregateAsAWhole()
    {
        var result = CompilerTestHelper.Run("""
            extern int putchar(int value);
            struct Token {
                private int id;
                public Token(int value) { id = value; }
                public readonly int Read() => id;
                ~Token() { putchar(id); }
            }
            static class State {
                public static readonly(Token) token = new Token(65);
                static State() { token = new Token(66); }
            }
            int main() { putchar(67); return State::token.Read() - 66; }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ACB", result.StandardOutput);
    }

    [Theory]
    [InlineData("interface I { static int Value { get; } }")]
    [InlineData("static interface I { int Value { get; } }")]
    public void StaticInterfacePropertiesCanBeImplementedByAutoProperties(string contract)
    {
        var result = CompilerTestHelper.Run(contract + """
            static class State : I { public static int Value { get; } = 42; }
            static class Read<T : I> { public static int Value() => T::Value; }
            int main() { return Read<State>::Value() - 42; }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ImportedPrivateGlobalsRemainFilePrivate()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check([
            new SourceFile("a.gf", "namespace State { private int value = 42; }"),
            new SourceFile("b.gf", "using State; int main() { return value; }")
        ]));
        Assert.Contains("private", error.Message);
    }
}
