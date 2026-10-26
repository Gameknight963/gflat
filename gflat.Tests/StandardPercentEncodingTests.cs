namespace gflat.Tests;

public sealed class StandardPercentEncodingTests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\0", "\\0") + "\"";

    private static SourceFile[] Sources(string program, bool strings = false)
    {
        var names = new List<string> { "Encoding/PercentEncoding.gf" };
        if (strings) names.AddRange(["Encoding/PercentEncodingText.gf", "IStringConvertable.gf", "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf", "Text/String.gf", "Text/ByteSearch.gf", "primitive_format.gf"]);
        return names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", program)).ToArray();
    }

    private static void Run(string program, bool strings = false)
    {
        var result = CompilerTestHelper.Run(Sources(program, strings));
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    // RFC 3986 sections 2.1, 2.3, 2.4, and 2.5 examples/rules.
    [InlineData(" ", "%20")]
    [InlineData("%", "%25")]
    [InlineData("~", "~")]
    [InlineData("Laguna Beach", "Laguna%20Beach")]
    [InlineData("AZaz09-._~", "AZaz09-._~")]
    [InlineData("a+b/c?x=y", "a%2Bb%2Fc%3Fx%3Dy")]
    public void RfcComponentExamplesPreserveOnlyUnreservedAscii(string input, string encoded)
    {
        Run($$"""
            using std; using std::Text; using std::Encoding;
            int main() {
                char[128] output; nuint written = 99;
                output[{{encoded.Length}}] = '?';
                if (!PercentEncoding::TryEncode({{Literal(input)}}, {{input.Length}}, output, {{encoded.Length}}, ref written) || written != (nuint){{encoded.Length}}) return 1;
                for (nuint i = 0; i < written; i++) if (output[i] != {{Literal(encoded)}}[i]) return 2;
                if (output[written] != '?') return 3;
                if (!PercentEncoding::TryDecode(output, written, output, 128, ref written) || written != (nuint){{input.Length}}) return 4;
                for (nuint i = 0; i < written; i++) if (output[i] != {{Literal(input)}}[i]) return 5;
                return 0;
            }
            """);
    }

    [Theory]
    [InlineData("café / 水 🙂")]
    [InlineData("reserved: /?#[]@!$&'()*+,;=")]
    [InlineData("alpha beta\0gamma")]
    public void ValidUtf8MatchesDotNetEscapeDataString(string input)
    {
        string encoded = Uri.EscapeDataString(input);
        int byteCount = System.Text.Encoding.UTF8.GetByteCount(input);
        Run($$"""
            using std; using std::Text; using std::Encoding;
            int main() {
                char[256] output; nuint written = 0;
                if (!PercentEncoding::TryEncode({{Literal(input)}}, {{byteCount}}, output, 256, ref written) || written != (nuint){{encoded.Length}}) return 1;
                for (nuint i = 0; i < written; i++) if (output[i] != {{Literal(encoded)}}[i]) return 2;
                return 0;
            }
            """);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("%0")]
    [InlineData("ok%GG")]
    [InlineData("%aZ")]
    [InlineData("%G0")]
    [InlineData("%%%")]
    [InlineData("%0G")]
    [InlineData("%FF%")]
    public void InvalidEscapesNeverWritePartialOutput(string input)
    {
        Run($$"""
            using std; using std::Text; using std::Encoding;
            int main() {
                char[32] output; for (int i = 0; i < 32; i++) output[i] = '?';
                nuint written = 99;
                if (PercentEncoding::TryGetDecodedLength({{Literal(input)}}, {{input.Length}}, ref written) || written != (nuint)0) return 1;
                if (PercentEncoding::TryDecode({{Literal(input)}}, {{input.Length}}, output, 32, ref written) || written != (nuint)0) return 2;
                for (int i = 0; i < 32; i++) if (output[i] != '?') return 3;
                return 0;
            }
            """);
    }

    [Fact]
    public void EveryByteRoundTripsWithoutAllocationsAndDecodesInPlace()
    {
        string expected = string.Concat(Enumerable.Range(0, 256).Select(i =>
            (i >= 65 && i <= 90) || (i >= 97 && i <= 122) || (i >= 48 && i <= 57) || i is 45 or 46 or 95 or 126
                ? ((char)i).ToString() : "%" + i.ToString("X2")));
        Run($$"""
            using std; using std::Text; using std::Encoding;
            static class Calls { public static int Allocations; }
            namespace Memory {
                public replace void*? Allocate(nuint size) { Calls::Allocations++; return null; }
                public replace void Free(void* data) nothrow { }
            }
            int main() {
                char[256] input; char[768] output;
                for (int i = 0; i < 256; i++) input[i] = (char)i;
                nuint written = 0;
                if (!PercentEncoding::TryEncode(input, 256, output, 768, ref written) || written != (nuint){{expected.Length}}) return 1;
                for (nuint i = 0; i < written; i++) if (output[i] != {{Literal(expected)}}[i]) return 2;
                if (!PercentEncoding::TryDecode(output, written, output, 256, ref written) || written != (nuint)256) return 3;
                for (int i = 0; i < 256; i++) if ((byte)output[i] != (byte)i) return 4;
                if (Calls::Allocations != 0) return 5;
                return 0;
            }
            """);
    }

    [Fact]
    public void ZerosPlusCaseAndSinglePassSemanticsAreExplicit()
    {
        Run("""
            using std; using std::Text; using std::Encoding;
            int main() {
                char[16] output; nuint written = 99;
                if (!PercentEncoding::TryDecode("A%00B+%2b", 9, output, 5, ref written) || written != (nuint)5) return 1;
                if (output[0] != 'A' || output[1] != '\0' || output[2] != 'B' || output[3] != '+' || output[4] != '+') return 2;
                if (!PercentEncoding::TryDecode("%7e%7E", 6, output, 2, ref written) || output[0] != '~' || output[1] != '~') return 3;
                if (!PercentEncoding::TryDecode("%2520", 5, output, 3, ref written) || written != (nuint)3 || output[0] != '%' || output[1] != '2' || output[2] != '0') return 4;
                if (!PercentEncoding::TryDecode("a\0b", 3, output, 3, ref written) || output[1] != '\0') return 5;
                return 0;
            }
            """);
    }

    [Fact]
    public void EmptyAndInsufficientCapacityLeaveBuffersUntouched()
    {
        Run("""
            using std; using std::Text; using std::Encoding;
            int main() {
                char[8] output; for (int i = 0; i < 8; i++) output[i] = '?';
                nuint written = 99;
                if (!PercentEncoding::TryEncode("", 0, output, 0, ref written) || written != (nuint)0) return 1;
                if (!PercentEncoding::TryDecode("", 0, output, 0, ref written) || written != (nuint)0) return 2;
                if (PercentEncoding::TryEncode(" ", 1, output, 2, ref written) || written != (nuint)0) return 3;
                if (PercentEncoding::TryDecode("ab%20", 5, output, 2, ref written) || written != (nuint)0) return 4;
                for (int i = 0; i < 8; i++) if (output[i] != '?') return 5;
                return 0;
            }
            """);
    }

    [Fact]
    public void BareCodecNeedsNoStdOrExceptionRuntime()
    {
        string ir = Compiler.Emit(Sources("""
            int main() {
                char[8] data; nuint written = 0;
                if (!std::Encoding::PercentEncoding::TryEncode(" ", 1, data, 8, ref written)) return 1;
                if (!std::Encoding::PercentEncoding::TryDecode(data, written, data, 8, ref written)) return 2;
                return 0;
            }
            """), options: new() { NoStdLib = true });
        Assert.DoesNotContain("__gflat_eh", ir);
        Assert.DoesNotContain("Memory$Allocate", ir);
    }

    [Fact]
    public void OwningConveniencesKeepBinaryZerosAndRejectMalformedEscapes()
    {
        Run("""
            using std; using std::Text; using std::Encoding;
            int main() {
                String encoded = PercentEncodingText::Encode("A\0B+", 4);
                if (encoded.Length != (nuint)8 || encoded.Data[1] != '%' || encoded.Data[5] != '%') return 1;
                String decoded = PercentEncodingText::Decode(encoded.Data, encoded.Length);
                if (decoded.Length != (nuint)4 || decoded.Data[0] != 'A' || decoded.Data[1] != '\0' || decoded.Data[2] != 'B' || decoded.Data[3] != '+') return 2;
                try { String bad = PercentEncodingText::Decode("ok%GG", 5); return 3; } catch (Exception* error) { }
                return 0;
            }
            """, strings: true);
    }
}
