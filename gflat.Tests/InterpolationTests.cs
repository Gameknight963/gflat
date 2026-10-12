using gflat.CompileExceptions;

namespace gflat.Tests;

public class InterpolationTests
{
    internal static SourceFile[] Sources(string program, bool libc = false)
    {
        var files = new List<SourceFile>();
        foreach (string file in new[] { "core/String.gf", "core/Formatting.gf" }.Concat(libc ? new[] { "libc/Formatting.gf" } : Array.Empty<string>()))
            files.Add(new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", file))));
        files.Add(new SourceFile("main.gf", program));
        return files.ToArray();
    }
    private static ExecutionResult Run(string program, bool libc = false) => CompilerTestHelper.Run(Sources(program, libc));

    [Fact]
    public void FormatsPrimitivesAndNestedStrings()
    {
        var result = Run("""
            using std;
            extern int printf(readonly(char)* text, ...);
            int main() {
                String text = s$"{{{42}}} {true} {false} {'!'} {s$"nested {7}"} {-9223372036854775807l - 1l} {18446744073709551615ul} {1.5f} {2.25d}";
                printf(c"%s", text.Data);
                return 0;
            }
            """, libc: true);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("{42} true false ! nested 7 -9223372036854775808 18446744073709551615 1.5 2.25", result.StandardOutput);
    }

    [Fact]
    public void QualifiedPrefixesGenericCloningAndLiteralByteLengths()
    {
        var result = Run("""
            std::String Format<T>(T value) throws { return std::s$"é\0{value}{{}}"; }
            int main() {
                std::String text = Format<int>(12);
                if (text.Length != (nuint)7) return 1;
                if (text[(nuint)2] != '\0') return 2;
                if (text[(nuint)3] != '1' || text[(nuint)6] != '}') return 3;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void HolesSupportQuotedBracesCommentsAndAdjacentInterpolations()
    {
        var result = Run("""
            using std;
            extern int printf(readonly(char)* text, ...);
            int Read(readonly(char)* p) { return (int)p[0]; }
            int main() {
                String first = s$"{Read(c"}") /* } */} {'{'} {1 // }
                + 2}";
                String second = s$"x";
                printf(c"%s|%s", first.Data, second.Data);
                return 0;
            }
            """);
        Assert.Equal("125 { 3|x", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ReceiverLifetimeAndHoleEvaluationOrder()
    {
        var result = Run("""
            using std;
            extern int printf(readonly(char)* text, ...);
            struct Value : IFormattable {
                public int n;
                public Value(int id) { n = id; printf(c"C%d;", n); }
                public readonly void Format(ITextWriter* output) throws { printf(c"F%d;", n); std::Format(n, output); }
                ~Value() { printf(c"D%d;", n); }
            }
            Value Make(int n) { return new Value(n); }
            int main() { String text = s$"{Make(1)}{Make(2)}"; printf(c"%s", text.Data); return 0; }
            """);
        Assert.Equal("C1;F1;D1;C2;F2;D2;12", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ReadonlyInterfaceManagedReceiverAndShortCircuit()
    {
        var result = Run("""
            using std;
            void*? __gflat_gc_alloc(nuint size) { return malloc(size); }
            struct Value : IFormattable {
                public readonly void Format(ITextWriter* output) throws { std::Format(3, output); }
            }
            int main() {
                Value value = new Value();
                readonly(IFormattable)* p = &value;
                Value^ managed = new^ Value();
                String a = s$"{p}{managed}";
                if (false && s$"{99}".Length == (nuint)2) return 1;
                if (true && s$"{42}".Length != (nuint)2) return 2;
                if (a.Length != (nuint)2) return 3; return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("$\"x\"", "custom string prefix")]
    [InlineData("c$\"x\"", "custom string prefix")]
    [InlineData("s$\"{c\"text\"}\"", "IFormattable")]
    [InlineData("s$\"{null}\"", "IFormattable")]
    public void UnsupportedValuesProduceDiagnostics(string expression, string expected)
    {
        var ex = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("using std; int main() { " + expression + "; return 0; }")));
        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData("s$\"}\"")]
    [InlineData("s$\"{1\"")]
    [InlineData("s$\"{}\"")]
    [InlineData("s$\"unterminated")]
    [InlineData("s$\"bad\\q\"")]
    public void MalformedInterpolationProducesCompilerErrors(string expression)
        => Assert.ThrowsAny<TypeCheckException>(() => Compiler.Check(Sources("using std; int main() { " + expression + "; return 0; }")));

    [Fact]
    public void DestinationMustOptIn()
    {
        Assert.Contains("ITextWriter", Assert.Throws<TypeCheckException>(() => Compiler.Check("""
            struct Plain { public static Plain operator p""(readonly(char)* p, nuint n) { return new Plain(); } }
            int main() { p$"x"; return 0; }
            """)).Message);

    }

    [Fact]
    public void UnhandledInterpolationConversionTerminatesAtNonThrowingBoundary()
    {
        var result = Run("""
            using std;
            extern int putchar(int c);
            struct Broken : IFormattable {
                public readonly void Format(ITextWriter* output) throws { throw new* Exception(); }
            }
            String F() { Broken b = new Broken(); return s$"{b}"; }
            int main() { try { F(); } catch { putchar(67); } putchar(88); return 0; }
            """);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public void HoleReadsParticipateInDefiniteAssignment()
        => Assert.Contains("unassigned", Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("using std; int main() { int value; s$\"{value}\"; return 0; }"))).Message, StringComparison.OrdinalIgnoreCase);
    [Fact]
    public void StringHolesPreserveEmbeddedNullBytes()
    {
        var result = Run("""
            using std;
            int main() {
                String value = s"a\0b";
                String text = s$"[{value}]";
                if (text.Length != (nuint)5) return 1;
                if (text[(nuint)2] != '\0' || text[(nuint)3] != 'b') return 2;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void FormattingExistingValuesDoesNotAllocateTextTemporaries()
    {
        var result = Run("""
            using std;
            extern void* malloc(nuint size);
            extern void free(void* p);
            extern int printf(readonly(char)* text, ...);
            namespace Memory {
                public replace void*? Allocate(nuint size) { printf(c"A"); return malloc(size); }
                public replace void Free(void* pointer) { free((void*)pointer); }
            }
            struct Counter : ITextWriter {
                public nuint count = 0;
                public void Write(readonly(char)* data, nuint length) throws { count += length; }
            }
            int main() {
                Counter writer = new Counter();
                std::Format(-42, &writer);
                std::Format(true, &writer);
                std::Format('x', &writer);
                std::Format(1.5d, &writer);
                if (writer.count != (nuint)11) return 1;
                return 0;
            }
            """, libc: true);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }
}
