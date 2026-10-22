using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ConstantCStringTests
{
    [Fact]
    public void ConstantIndexingUsesTheSameUtf8BytesEscapesAndTerminatorAsRuntime()
    {
        var result = CompilerTestHelper.Run("""
            const int first = (int)"é"[0];
            const int second = (int)"é"[1];
            const int end = (int)"é"[2];
            const int newline = (int)"\n"[0];
            const int slash = (int)"\\"[0];
            const int embedded = (int)"a\0b"[2];
            int main() {
                readonly(char)* text = "é";
                if (first != (int)text[0] || second != (int)text[1] || end != 0) return 1;
                if (newline != 10 || slash != 92 || embedded != 98) return 2;
                if ((byte)text[0] != (byte)195 || (byte)text[1] != (byte)169) return 3;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ConstantPointerOffsetsSurviveEmissionReturnsDefaultsAndObjectFields()
    {
        var result = CompilerTestHelper.Run("""
            extern int puts(readonly(char)* text);
            const immutable(char*) tail = "hello" + 1;
            readonly(char)* Default(readonly(char)* value = "hello" + 1) => value;
            struct View {
                public readonly(char)* text;
                public const View(readonly(char)* value) { text = value; }
            }
            const View view = new View("hello" + 1);
            const immutable(char*) Rest() => "hello" + 1;
            const immutable(char*) returned = Rest();
            int main() {
                puts(tail);
                if (tail[0] != 'e' || tail[4] != '\0') return 1;
                if (Default() != tail || view.text != tail || returned != tail) return 2;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ello\n", result.StandardOutput.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ConstantPointerArithmeticPreservesStorageAndSupportsOnePastAddresses()
    {
        var result = CompilerTestHelper.Run("""
            const bool Check() {
                readonly(char)* text = "hello";
                text += 3;
                if (*text != 'l' || text[-3] != 'h') return false;
                if (*text-- != 'l' || *--text != 'e') return false;
                text = 6 + "hello";
                if (text[-1] != '\0') return false;
                text -= 6;
                if (text != "hello") return false;
                return ("hello" + 5) - text == 5l && text < text + 1;
            }
            const bool folded = Check();
            int main() { if (!folded || !Check()) return 1; return 0; }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ConstantAndRuntimePointerEqualityUseInternedDecodedStorage()
    {
        var result = CompilerTestHelper.Run("""
            const bool same = "hello" == "hello";
            const bool escaped = "'" == "\'";
            const bool different = "hello" == "world";
            const bool embedded = "a\0b" == "a\0c";
            const bool suffix = ("ab" + 1) == "b";
            bool Equal(readonly(char)* a, readonly(char)* b) => a == b;
            int main() {
                if (!same || !Equal("hello", "hello")) return 1;
                if (!escaped || !Equal("'", "\'")) return 2;
                if (different || Equal("hello", "world")) return 3;
                if (embedded || Equal("a\0b", "a\0c")) return 4;
                if (suffix || Equal("ab" + 1, "b")) return 5;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("const int invalid = (int)\"abc\"[4];", "outside")]
    [InlineData("const int invalid = (int)\"é\"[3];", "outside")]
    [InlineData("const int invalid = (int)(\"abc\" + 4)[0];", "outside")]
    [InlineData("const int invalid = (int)\"abc\"[18446744073709551615ul];", "outside")]
    [InlineData("const immutable(char*) invalid = \"abc\" - 1;", "outside")]
    [InlineData("const immutable(char*) invalid = \"abc\" + 5;", "outside")]
    [InlineData("const bool invalid = \"abc\" < \"def\";", "distinct")]
    [InlineData("const long invalid = \"abc\" - \"def\";", "distinct")]
    [InlineData("const bool invalid = (\"abc\" + 4) == \"def\";", "one-past")]
    [InlineData("const immutable(int*) invalid = (readonly(int)*)\"abcdefgh\" + 1;", "cast")]
    public void InvalidOrUnrepresentableConstantPointerOperationsProduceDiagnostics(string source, string expected)
        => Assert.Contains(expected, Assert.Throws<TypeCheckException>(() => Compiler.Check(source)).Message, StringComparison.OrdinalIgnoreCase);
}
