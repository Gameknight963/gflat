namespace gflat.Tests;

public sealed class StandardExtensionTests
{
    [Fact]
    public void FloatingFormattingSupportsTheSameInstanceSyntaxWithoutStringDependencies()
    {
        var sources = ConsoleWriterTests.Sources("""
            using std; using std::Text;
            int main() {
                Console::Writer output = new Console::Writer();
                float single = 1.25f;
                single.Format(&output);
                Console::Write("|");
                double wide = 2.5d;
                wide.Format(&output);
                Console::Write("|");
                std::Format(single, &output);
                return 0;
            }
            """).Append(new SourceFile("libc/Formatting.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "libc", "Formatting.gf"))));
        var result = CompilerTestHelper.Run(sources);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1.25|2.5|1.25", result.StandardOutput);
    }

    private static SourceFile[] Sources(string program) => InterpolationTests.Sources(program).Concat(
        new[] { "core/Parsing.gf", "core/StringParsing.gf" }.Select(file =>
            new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", file))))).ToArray();

    [Fact]
    public void CStringAndBorrowedStringParsingShareTheSameGrammar()
    {
        Assert.Equal(42, CompilerTestHelper.Run(Sources("""
            using std; using std::Text;
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
            using std; using std::Text;
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
            using std; using std::Text;
            extern void* malloc(nuint size);
            extern void free(void* storage);
            extern int printf(immutable(char*) text, ...);
            namespace Memory {
                public replace void*? Allocate(nuint size) { printf("A"); return malloc(size); }
                public replace void Free(void* storage) nothrow { free(storage); }
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
