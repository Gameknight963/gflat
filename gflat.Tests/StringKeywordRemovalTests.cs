using gflat.CompileExceptions;

namespace gflat.Tests;

public class StringKeywordRemovalTests
{
    [Fact]
    public void StringIsAnOrdinaryIdentifierAndCanNameAUserType()
    {
        Assert.Equal(TokenKind.Identifier, Lexer.Tokenize("string")[0].Kind);
        Assert.Equal(0, CompilerTestHelper.Run("""
            struct string { public int value; }
            int main() {
                string text = new string(); text.value = 42;
                int string = text.value;
                if (string != 42) return 1; return 0;
            }
            """).ExitCode);
    }

    [Fact]
    public void UndeclaredStringTypeIsRejected()
        => Assert.Throws<TypeCheckException>(() => CompilerTestHelper.Check("int main() { string value; return 0; }"));

    [Fact]
    public void LiteralFormsKeepTheirExistingTypes()
        => Assert.Equal(0, CompilerTestHelper.Run("""
            int main() {
                char[3] array = "hi";
                readonly(char)* pointer = c"hi";
                if (array[1] != pointer[1]) return 1; return 0;
            }
            """).ExitCode);

}
