namespace gflat.Tests;

public class InterpolationLifetimeTests
{
    [Fact]
    public void DeeplyNestedInterpolationTokenizesWithoutRepeatedExpansion()
    {
        string source = "value";
        for (int depth = 0; depth < 32; depth++) source = "t$\"{" + source + "}\"";
        var tokens = Lexer.Tokenize(source);
        Assert.Equal(32, tokens.Count(t => t.Kind == TokenKind.InterpolatedStringStart));
        Assert.Equal(32, tokens.Count(t => t.Kind == TokenKind.InterpolatedStringEnd));
        Assert.Single(tokens, t => t.Kind == TokenKind.Identifier && t.Text == "value");
    }

    private const string Tracking = """
        extern int printf(readonly(char)* text, ...);
        namespace Allocator {
            public replace void*? Allocate(nuint size) { printf(c"A"); return malloc(size); }
            public replace void Free(void* p) { printf(c"F"); free(p); }
        }
        struct Destination : ITextWriter {
            public int calls;
            public Destination() { calls = 0; }
            public static Destination operator t""(readonly(char)* p, nuint n) { return new Destination(); }
            public void Write(readonly(char)* p, nuint n) throws {
                if (n != (nuint)0) { printf(c"P"); throw new* Exception(); }
            }
            ~Destination() { printf(c"D"); }
        }
        struct Value : IFormattable {
            public readonly void Format(ITextWriter* output) throws {
                output.Write(c"x", 1);
            }
            ~Value() { printf(c"V"); }
        }
        """;

    [Fact]
    public void WriteFailureDestroysReceiverAndDestination()
    {
        var result = CompilerTestHelper.Run(Tracking + """
            int main() { try { t$"{new Value()}"; } catch { printf(c"C"); } return 0; }
            """);
        // Write, exception allocation, receiver cleanup, destination cleanup,
        // catch, and exception release. No temporary text allocation exists.
        Assert.Equal("PAVDCF", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void FormattingFailureDestroysReceiverAndDestination()
    {
        var result = CompilerTestHelper.Run(Tracking.Replace("output.Write(c\"x\", 1);", "throw new* Exception();") + """
            int main() { try { t$"{new Value()}"; } catch { printf(c"C"); } return 0; }
            """);
        Assert.Equal("AVDCF", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void CustomClassDestinationWorksWithoutStandardLibrary()
    {
        string source = Tracking.Replace("struct Destination", "class Destination").Replace(
            "if (n != (nuint)0) { printf(c\"P\"); throw new* Exception(); }",
            "printf(c\"%.*s\", (int)n, p);");
        var result = CompilerTestHelper.Run(source + "int main() { t$\"a{new Value()}b\"; return 0; }");
        Assert.Equal("axVbD", result.StandardOutput);
        Assert.Equal(0, result.ExitCode);
    }
}
