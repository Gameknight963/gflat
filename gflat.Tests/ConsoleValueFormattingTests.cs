using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ConsoleValueFormattingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrimitiveOverloadsWriteExpectedTextIncludingFullIntegerRanges(int optimization)
    {
        ExecutionResult result = Run("""
            int main()
            {
                Console::Write((sbyte)-128); Console::Write('|');
                Console::Write((byte)255); Console::Write('|');
                Console::Write((short)-32768); Console::Write('|');
                Console::Write((ushort)65535); Console::Write('|');
                Console::Write(-2147483647 - 1); Console::Write('|');
                Console::Write(4294967295u); Console::Write('|');
                Console::Write(-9223372036854775807l - 1l); Console::Write('|');
                Console::Write(18446744073709551615ul); Console::Write('|');
                Console::Write((nint)(-9223372036854775807l - 1l)); Console::Write('|');
                Console::Write((nuint)18446744073709551615ul); Console::Write('|');
                Console::Write(true); Console::Write('|');
                Console::Write(false); Console::Write('|');
                Console::Write(1.5f); Console::Write('|');
                Console::Write(0.125d); Console::Write('|');
                extralong maximum = ((extralong)1 << 126) - 1;
                maximum = maximum * 2 + 1;
                extralong minimum = -maximum - 1;
                Console::Write(maximum); Console::Write('|');
                Console::Write(minimum); Console::WriteLine('\0');
                Console::WriteLine((sbyte)-128);
                Console::WriteLine((byte)255);
                Console::WriteLine((short)-32768);
                Console::WriteLine((ushort)65535);
                Console::WriteLine(-2147483647 - 1);
                Console::WriteLine(4294967295u);
                Console::WriteLine(-9223372036854775807l - 1l);
                Console::WriteLine(18446744073709551615ul);
                Console::WriteLine((nint)(-9223372036854775807l - 1l));
                Console::WriteLine((nuint)18446744073709551615ul);
                Console::WriteLine(true);
                Console::WriteLine(false);
                Console::WriteLine(1.5f);
                Console::WriteLine(0.125d);
                Console::WriteLine(maximum);
                Console::WriteLine(minimum);
                Console::Flush();
                return 0;
            }
            """, optimization);
        string[] expected =
        [
            "-128", "255", "-32768", "65535", "-2147483648", "4294967295",
            "-9223372036854775808", "18446744073709551615", "-9223372036854775808",
            "18446744073709551615", "true", "false",
            "1.5", "0.125", "170141183460469231731687303715884105727",
            "-170141183460469231731687303715884105728"
        ];
        string newline = Environment.NewLine;
        Assert.Equal(string.Join('|', expected) + "\0" + newline + string.Join(newline, expected) + newline,
            result.StandardOutput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FormattableBorrowingSupportsNoncopyableOwnersAndAvoidsCopies(int optimization)
    {
        ExecutionResult result = Run("""
            using Standard::Text;

            int copies = 0;
            int formats = 0;
            int live = 0;
            struct Payload : IFormattable
            {
                private String text;
                public Payload() { text = s"A\0B"; live++; }
                public copy Payload(immutable(Payload)* source) { text = source.text; copies++; live++; }
                public immutable void Format(ITextWriter* output) { formats++; output.Write(text.Data, text.Length); }
                ~Payload() { live--; }
            }
            struct Noncopyable : IFormattable
            {
                public Noncopyable() { live++; }
                public immutable void Format(ITextWriter* output) { formats++; output.Write("owned", 5); }
                ~Noncopyable() { live--; }
            }
            void Forward<T : IFormattable>(ref immutable(T) value)
            {
                Console::WriteLine(ref value);
            }
            int main()
            {
                {
                    Payload payload = new();
                    Console::Write(ref payload);
                    Forward(ref payload);
                    if (copies != 0 || formats != 2 || live != 1) return 10;
                    Noncopyable owned = new();
                    Console::Write(ref owned);
                    Console::WriteLine(ref owned);
                    if (copies != 0 || formats != 4 || live != 2) return 11;
                    immutable(Noncopyable) protectedValue = new();
                    Console::WriteLine(ref protectedValue);
                    if (copies != 0 || formats != 5 || live != 3) return 13;
                }
                if (live != 0) return 12;
                Console::Flush();
                return 0;
            }
            """, optimization);
        Assert.Equal("A\0BA\0B" + Environment.NewLine + "ownedowned" + Environment.NewLine + "owned" + Environment.NewLine,
            result.StandardOutput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FormatterFailureKeepsWrittenPrefixAndSkipsTheNewline(int optimization)
    {
        ExecutionResult result = Run("""
            using Standard::IO;

            class Failure : IOException { public Failure() : base("format failed") { code = 71; } }
            struct Broken : IFormattable
            {
                public immutable void Format(ITextWriter* output)
                {
                    output.Write("prefix", 6);
                    throw new Failure();
                }
            }
            int main()
            {
                Broken value = new();
                bool caught = false;
                try { Console::WriteLine(ref value); }
                catch (ref Failure error) { caught = error.GetCode() == 71; }
                if (!caught) return 20;
                Console::Write("tail");
                Console::Flush();
                return 0;
            }
            """, optimization);
        Assert.Equal("prefixtail", result.StandardOutput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExistingCStringCountedStringAndRefStringCallsKeepTheirMeaning(int optimization)
    {
        ExecutionResult result = Run("""
            using Standard::Text;

            int main()
            {
                Console::Write(0); Console::Write('|'); Console::WriteLine(1);
                Console::WriteLine(42);
                char[4] characters = ['m', 'u', 't', '\0'];
                char* pointer = characters;
                Console::WriteLine(pointer);
                Console::WriteLine("first\0ignored");
                Console::WriteLine("A\0B", 3);
                String text = s"counted\0tail";
                Console::WriteLine(ref text);
                Console::WriteLine();
                Console::Flush();
                return 0;
            }
            """, optimization);
        string newline = Environment.NewLine;
        Assert.Equal($"0|1{newline}42{newline}mut{newline}first{newline}A\0B{newline}counted\0tail{newline}{newline}",
            result.StandardOutput);
    }

    [Fact]
    public void FormattableOwnersRequireExplicitBorrowing()
    {
        var program = new SourceFile("console-format-borrow.gf", """
            struct Owned : IFormattable
            {
                public immutable void Format(ITextWriter* output) { output.Write("owned", 5); }
                ~Owned() {}
            }
            int main()
            {
                Owned value = new();
                Console::WriteLine(value);
                return 0;
            }
            """);
        Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(program), options: new() { NoStdLib = true }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PrimitiveOutputWorksWhenApplicationAllocationIsDenied(int optimization)
    {
        ExecutionResult result = Run("""
            int attempts = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint count) { attempts++; return null; }
                public replace void Free(void* storage) nothrow {}
            }
            int main()
            {
                Console::WriteLine(42);
                Console::WriteLine(true);
                Console::WriteLine(1.5);
                extralong wide = ((extralong)1 << 100) + 3;
                Console::WriteLine(wide);
                Console::Flush();
                return attempts;
            }
            """, optimization);
        Assert.Equal(string.Join(Environment.NewLine, "42", "true", "1.5", "1267650600228229401496703205379", ""),
            result.StandardOutput);
    }

    [Fact]
    public void IntegerAndBorrowedOutputCheckWithoutStringWideOrFloatingFormatterSources()
    {
        string[] selected =
        [
            "core/IO/IOException.gf", "core/IO/EndOfStreamException.gf", "core/Text/BufferTooSmallException.gf",
            "core/primitive_format.gf", "libc/Console/Console.gf", "libc/Console/standard_writers.gf",
            "libc/Console/value_formatting.gf"
        ];
        var sources = StandardLibrary.AutomaticFiles.Concat(selected).Distinct(StringComparer.Ordinal)
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("console-format-selected.gf", """
                struct Label : IFormattable
                {
                    public immutable void Format(ITextWriter* output) { output.Write("label", 5); }
                }
                int main()
                {
                    Label label = new();
                    Console::WriteLine(42);
                    Console::WriteLine(ref label);
                    return 0;
                }
                """));
        Compiler.Check(sources, options: new() { NoStdLib = true });
    }

    private static IEnumerable<SourceFile> Sources(SourceFile program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).Append(program);

    private static ExecutionResult Run(string code, int optimization)
    {
        string ir = Compiler.Emit(Sources(new SourceFile("console-formatting-tests.gf", code)),
            options: new() { NoStdLib = true, InternalizeFunctions = true });
        return CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
