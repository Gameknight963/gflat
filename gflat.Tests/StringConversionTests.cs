namespace gflat.Tests;

public sealed class StringConversionTests
{
    private static ExecutionResult Run(string source) => CompilerTestHelper.Run(InterpolationTests.Sources(source));

    [Fact]
    public void StringConversionOwnsAnIndependentBufferAndPreservesNullBytes()
    {
        var result = Run("""
            using std;
            int main() {
                String original = s"a\0b";
                immutable(IStringConvertable)* view = &original;
                String converted = view.ToString();
                original[0] = 'X';
                original.Clear();
                if (converted.Length != (nuint)3 || converted[0] != 'a' || converted[1] != '\0' || converted[2] != 'b') return 1;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ConversionOnlyValuesInterpolateThroughValuesAndInterfaceViews()
    {
        var result = Run("""
            using std;
            struct Value : IStringConvertable {
                public immutable String ToString() => s"a\0b";
            }
            int main() {
                Value value = new Value();
                immutable(IStringConvertable)* view = &value;
                String result = s$"[{value}]{view}";
                if (result.Length != (nuint)8 || result[2] != '\0' || result[6] != '\0') return 1;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void DirectFormattingTakesPrecedenceOverConversionAndBorrowedCString()
    {
        var result = Run("""
            using std;
            struct Value : IStringConvertable, IFormattable {
                public immutable String ToString() { throw new Exception("unexpected conversion"); }
                public immutable immutable(char*) ToCString() { throw new Exception("unexpected C string"); }
                public immutable void Format(ITextWriter* output) { output.Write("direct", 6); }
            }
            int main() {
                String result = s$"{new Value()}";
                if (result.Length != (nuint)6 || result[0] != 'd') return 1;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
    }

    private const string Tracking = """
        using std;
        extern void*? malloc(nuint size);
        extern void free(void* storage);
        extern int printf(readonly(char)* text, ...);
        namespace Memory {
            public replace void*? Allocate(nuint size) { printf("A"); return malloc(size); }
            public replace void Free(void* p) nothrow { printf("F"); free(p); }
        }
        struct Destination : ITextWriter {
            public static Destination operator t""(readonly(char)* p, nuint n) { return new Destination(); }
            public void Write(readonly(char)* p, nuint n) {
                if (n != (nuint)0) { printf("P"); throw new Exception(); }
            }
            ~Destination() { printf("D"); }
        }
        struct Value : IStringConvertable {
            public immutable String ToString() { printf("T"); return s"text"; }
            ~Value() { printf("V"); }
        }
        """;

    [Fact]
    public void WriteFailureDestroysConvertedStringReceiverAndDestinationOnce()
    {
        var result = Run(Tracking + "int main() { try { t$\"{new Value()}\"; } catch { printf(\"C\"); } return 0; }");
        Assert.Equal("TAPAFVDCF", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ConversionFailureDestroysReceiverAndDestinationWithoutDestroyingUnconstructedString()
    {
        var result = Run(Tracking.Replace("return s\"text\";", "throw new Exception();") +
            "int main() { try { t$\"{new Value()}\"; } catch { printf(\"C\"); } return 0; }");
        Assert.Equal("TAVDCF", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void SuccessfulConversionCleansUpBeforeEvaluatingTheNextHole()
    {
        string tracking = Tracking.Replace("if (n != (nuint)0) { printf(\"P\"); throw new Exception(); }", "if (n != (nuint)0) printf(\"P\");");
        var result = Run(tracking + "int main() { t$\"{new Value()}{new Value()}\"; return 0; }");
        Assert.Equal("TAPFVTAPFVD", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }
}
