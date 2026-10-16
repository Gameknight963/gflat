using gflat.CompileExceptions;

namespace gflat.Tests;

public class CharacterLiteralTests
{
    [Fact]
    public void CharacterOrderingUsesTheSignedStorageValueAtCompileTimeAndRuntime()
    {
        var result = CompilerTestHelper.Run("""
            const bool folded = 'é' < '\0';
            int main() {
                char high = 'é';
                char zero = '\0';
                if (!folded || !(high < zero)) return 1;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("'a'", 97)]
    [InlineData("'\\n'", 10)]
    [InlineData("'\\r'", 13)]
    [InlineData("'\\t'", 9)]
    [InlineData("'\\0'", 0)]
    [InlineData("'\\\\'", 92)]
    [InlineData("'\\\''", 39)]
    [InlineData("'\\\"'", 34)]
    [InlineData("'é'", -23)]
    public void ConstantAndRuntimeDecodingAgree(string literal, int expected)
    {
        var result = CompilerTestHelper.Run($$"""
            const int folded = (int){{literal}};
            enum Value { Character = {{literal}} }
            int main() {
                if (folded != {{expected}}) return 1;
                if ((int){{literal}} != folded) return 2;
                if ((int)Value::Character != folded) return 3;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("'\\q'")]
    [InlineData("'Ā'")]
    [InlineData("'\n'")]
    public void UnsupportedEscapesAndNonfittingCharactersProduceSourceErrors(string literal)
        => Assert.Throws<TypeCheckException>(() => Compiler.Emit("int main() { char value = " + literal + "; return 0; }"));
}
