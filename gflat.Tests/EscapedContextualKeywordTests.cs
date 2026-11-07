namespace gflat.Tests;

public sealed class EscapedContextualKeywordTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EscapedOperatorNamesWorkInConstantAndRuntimeExpressions(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            const int Read()
            {
                int @move = 21;
                int @ref = 21;
                return @move + @ref;
            }
            const int folded = Read();
            int main() => folded + Read() - 84;
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("move")]
    [InlineData("copy")]
    [InlineData("extension")]
    [InlineData("implementation")]
    [InlineData("explicit")]
    [InlineData("constrained")]
    [InlineData("this")]
    public void EscapedTypeNamesAreNotDeclarationOrParameterModifiers(string name)
        => Compiler.Check($$"""
            struct @{{name}} { public int Value; }
            struct Holder { public @{{name}}* Pointer; }
            @{{name}} Read(@{{name}} value) => value;
            """);

    [Theory]
    [InlineData("#define VALUE @move\nint Read(){int @move=42;return VALUE;}")]
    [InlineData("#define ID(value) value\nint Read(){int @ref=42;return ID(@ref);}")]
    public void ExpansionRetainsIdentifierEscapes(string code) => Compiler.Check(code);

    [Fact]
    public void InterpolationRetainsIdentifierEscapes()
        => Compiler.Check(InterpolationTests.Sources("""
            using Standard;
            using Standard::Text;

            String Render() { int @move = 42; return s$"{@move}"; }
            """));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SuppressionGroupsRetainContextualOperators(int optimization)
    {
        var result = CompilerTestHelper.Run("""
            int Read(ref int value) => value;
            int main()
            {
                int original = 42;
                int transferred = @(move) original;
                return Read(@(ref) transferred) - 42;
            }
            """, optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("set")]
    public void EscapedNamesCannotActAsAccessorKeywords(string name)
        => Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Check(
            $$"""struct Item { public int Value { @{{name}} => 42; } }"""));

    [Fact]
    public void LexerRetainsEscapesThroughInterpolationAndColumnAssignment()
    {
        var tokens = Lexer.Tokenize("@ref ref s$\"{@move}\"");
        Assert.True(tokens.Single(token => token.Text == "ref" && token.Start == 1).IsEscapedIdentifier);
        Assert.False(tokens.Single(token => token.Text == "ref" && token.Start == 5).IsEscapedIdentifier);
        Assert.True(tokens.Single(token => token.Text == "move").IsEscapedIdentifier);
    }
}
