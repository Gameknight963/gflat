namespace gflat.Tests;

public sealed class StandardExtensionTests
{
    private static SourceFile[] Sources(string program) => InterpolationTests.Sources(program).Concat(
        new[] { "core/Parsing.gf", "core/StringParsing.gf" }.Select(file =>
            new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", file))))).ToArray();

    [Fact]
    public void CStringAndBorrowedStringParsingShareTheSameGrammar()
    {
        Assert.Equal(42, CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                int number = 0;
                if (!std::TryParseInt("40", &number) || number != 40) return 1;
                if (std::ParseInt("42") != 42) return 2;
                char[3] counted = ['4', '2', 'x'];
                if (!std::TryParseInt(counted, 2, &number) || number != 42) return 3;
                String text = s"+42";
                if (!text.TryParseInt(&number) || number != 42) return 4;
                return text.ParseInt();
            }
            """)).ExitCode);
    }

    [Fact]
    public void CountedStringParsingRejectsEmbeddedNull()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                char[3] bytes = ['4', '\0', '2'];
                String text = String::From(bytes, 3);
                int value = 99;
                if (text.TryParseInt(&value) || value != 0) return 1;
                try { text.ParseInt(); return 2; }
                catch (ref Exception error) { return 0; }
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void PrimitiveFormattingWritesIntoTheExistingWriter()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            extern void* malloc(nuint size);
            extern void free(void* storage);
            extern int printf(readonly(char*) text, ...);
            namespace Memory {
                public replace void*? Allocate(nuint size) { printf("A"); return malloc(size); }
                public replace void Free(void* storage) { free(storage); }
            }
            struct Writer : ITextWriter {
                public nuint length;
                public void Write(readonly(char)* text, nuint count) { length += count; }
            }
            int main() {
                Writer writer = new Writer();
                int number = -42;
                number.Format(&writer);
                true.Format(&writer);
                'x'.Format(&writer);
                if (writer.length != (nuint)8) return 1;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }
}
