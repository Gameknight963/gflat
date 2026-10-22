using gflat.CompileExceptions;

namespace gflat.Tests;

public class PlainStringTests
{
    [Fact]
    public void LiteralsReturnStablePointersAndUsePointerSizedType()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            immutable(char*) Text() => "hello";
            int main() {
                immutable(char*) text = Text();
                if (text[4] != 'o' || text[5] != '\0') return 1;
                return 42;
            }
            """).ExitCode);
    }

    [Fact]
    public void LiteralTypeIsAReadonlyPointer()
    {
        var snapshot = new AnalysisSnapshot([new SourceFile("literal.gf", "immutable(char*) Text() => \"hello\";")]);
        var literal = snapshot.Nodes.Select(n => n.Node).OfType<gflat.ast.LiteralExpression>().Single(n => n.Token.Text == "\"hello\"");
        Assert.Equal("readonly(char)*", TypeChecker.TypeName(snapshot.Checker!.GetType(literal)));
    }

    [Theory]
    [InlineData("char[6] buffer = \"hello\";", 6)]
    [InlineData("char[10] buffer = \"hello\";", 10)]
    [InlineData("char[] buffer = \"hello\";", 6)]
    public void BufferInitializationCopiesAndZeroPads(string declaration, int size)
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            int main() {
            """ + declaration + $$"""
                immutable(char*) original = "hello";
                buffer[0] = 'H';
                if (buffer[0] != 'H' || original[0] != 'h') return 1;
                for (int i = 5; i < {{size}}; i++) if (buffer[i] != '\0') return 2;
                return 42;
            }
            """).ExitCode);
    }

    [Fact]
    public void BufferCopiesWorkForReturnsFieldsArgumentsAndConstants()
    {
        Assert.Equal(42, CompilerTestHelper.Run("""
            const char[10] Constant = "hello";
            char[10] Make() => "hello";
            int Check(char[10] text) { text[0] = 'H'; if (text[0] == 'H' && text[9] == '\0') return 42; return 1; }
            struct Data { public char[10] text = "hello"; }
            int main() {
                Data data = new Data();
                char[10] text = Make();
                if (Constant[0] != 'h' || Constant[9] != '\0' || text[5] != '\0' || data.text[9] != '\0') return 2;
                return Check("hello");
            }
            """).ExitCode);
    }

    [Theory]
    [InlineData("char* text = \"hello\";")]
    [InlineData("char[5] text = \"hello\";")]
    [InlineData("char[2] text = \"é\";")]
    [InlineData("int[6] text = \"hello\";")]
    [InlineData("\"hello\"[0] = 'H';")]
    public void InvalidMutationsAndBufferConversionsAreRejected(string statement)
        => Assert.Throws<TypeCheckException>(() => Compiler.Check("int main() { " + statement + " return 0; }"));

    [Fact]
    public void CStringPrefixIsNoLongerBuiltIn()
    {
        Assert.Contains("Unknown string prefix", Assert.Throws<TypeCheckException>(() => Compiler.Check("int main() { c\"hello\"; return 0; }")).Message);
        Assert.Equal(42, CompilerTestHelper.Run("""
            struct Text {
                public int length;
                public Text(int size) { length = size; }
                public static Text operator c""(immutable(char*) text, nuint length) { return new Text((int)length); }
            }
            int main() { Text text = c"abc"; if (text.length == 3) return 42; return 1; }
            """).ExitCode);
    }
}
