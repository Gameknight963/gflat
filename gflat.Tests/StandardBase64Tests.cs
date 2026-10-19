namespace gflat.Tests;

public sealed class StandardBase64Tests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Run(string program, bool text = false)
    {
        var names = new List<string> { "core/Base64.gf" };
        if (text) names.AddRange(["core/Base64Text.gf", "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing.gf", "core/String.gf", "core/Formatting.gf"]);
        var files = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", program));
        var result = CompilerTestHelper.Run(files);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "Zg==")]
    [InlineData("fo", "Zm8=")]
    [InlineData("foo", "Zm9v")]
    [InlineData("foob", "Zm9vYg==")]
    [InlineData("fooba", "Zm9vYmE=")]
    [InlineData("foobar", "Zm9vYmFy")]
    public void RfcVectorsAndExactCapacityHaveNoImplicitTerminator(string plain, string encoded)
    {
        Run($$"""
            using std;
            int main() {
                char[32] encoded; char[32] decoded;
                for (int i = 0; i < 32; i++) { encoded[i] = '?'; decoded[i] = '?'; }
                nuint written = 99;
                if (!Base64::TryEncode({{Literal(plain)}}, {{plain.Length}}, encoded, {{encoded.Length}}, ref written) || written != (nuint){{encoded.Length}}) return 1;
                for (nuint i = 0; i < written; i++) if (encoded[i] != {{Literal(encoded)}}[i]) return 2;
                if (encoded[written] != '?') return 3;
                if (!Base64::TryDecode(encoded, written, decoded, {{plain.Length}}, ref written) || written != (nuint){{plain.Length}}) return 4;
                for (nuint i = 0; i < written; i++) if (decoded[i] != {{Literal(plain)}}[i]) return 5;
                if (decoded[written] != '?') return 6;
                return 0;
            }
            """);
    }

    [Theory]
    [InlineData("=")]
    [InlineData("====")]
    [InlineData("A===")]
    [InlineData("AA=A")]
    [InlineData("TQ==AAAA")]
    [InlineData("TQ==\n")]
    [InlineData("TQ")]
    [InlineData("TR==")]
    [InlineData("TWF=")]
    [InlineData("TQ_=")]
    [InlineData("Zm9vTR==")]
    public void MalformedInputNeverWritesPartialOutput(string encoded)
    {
        string literal = Literal(encoded).Replace("\n", "\\n");
        Run($$"""
            using std;
            int main() {
                char[32] output;
                for (int i = 0; i < 32; i++) output[i] = '?';
                nuint written = 99;
                if (Base64::TryGetDecodedLength({{literal}}, {{encoded.Length}}, ref written) || written != (nuint)0) return 1;
                if (Base64::TryDecode({{literal}}, {{encoded.Length}}, output, 32, ref written) || written != (nuint)0) return 2;
                for (int i = 0; i < 32; i++) if (output[i] != '?') return 3;
                return 0;
            }
            """);
    }

    [Fact]
    public void EveryByteRoundTripsAndSupportsInPlaceDecoding()
    {
        byte[] input = Enumerable.Range(0, 257).Select(i => (byte)i).ToArray();
        string expected = Convert.ToBase64String(input);
        Run($$"""
            using std;
            int main() {
                char[257] input; char[344] output;
                for (int i = 0; i < 257; i++) input[i] = (char)i;
                nuint written = 0;
                if (!Base64::TryEncode(input, 257, output, 344, ref written) || written != (nuint)344) return 1;
                for (nuint i = 0; i < written; i++) if (output[i] != {{Literal(expected)}}[i]) return 2;
                if (!Base64::TryDecode(output, 344, output, 344, ref written) || written != (nuint)257) return 3;
                for (int i = 0; i < 257; i++) if ((byte)output[i] != (byte)i) return 4;
                return 0;
            }
            """);
    }

    [Fact]
    public void CapacityAndLengthOverflowFailuresLeaveOutputUntouched()
    {
        Run("""
            using std;
            int main() {
                char[8] output; for (int i = 0; i < 8; i++) output[i] = '?';
                nuint written = 99;
                if (Base64::TryEncode("a", 1, output, 3, ref written) || written != (nuint)0) return 1;
                if (Base64::TryDecode("Zm9v", 4, output, 2, ref written) || written != (nuint)0) return 2;
                for (int i = 0; i < 8; i++) if (output[i] != '?') return 3;
                nuint maximum = (nuint)0 - 1;
                if (Base64::TryGetEncodedLength(maximum, ref written) || written != (nuint)0) return 4;
                nuint boundary = (maximum / 4) * 3;
                if (!Base64::TryGetEncodedLength(boundary, ref written) || written != maximum - 3) return 5;
                if (Base64::TryGetEncodedLength(boundary + 1, ref written) || written != (nuint)0) return 6;
                return 0;
            }
            """);
    }

    [Fact]
    public void OwningStringHelpersPreserveZerosAndRejectMalformedInput()
    {
        Run("""
            using std;
            int main() {
                String encoded = Base64Text::Encode("A\0B", 3);
                if (encoded.Length != (nuint)4 || encoded.Data[0] != 'Q' || encoded.Data[1] != 'Q' || encoded.Data[2] != 'B' || encoded.Data[3] != 'C') return 1;
                String decoded = Base64Text::Decode(encoded.Data, encoded.Length);
                if (decoded.Length != (nuint)3 || decoded.Data[0] != 'A' || decoded.Data[1] != '\0' || decoded.Data[2] != 'B' || decoded.Data[3] != '\0') return 2;
                String empty = Base64Text::Decode("", 0);
                if (empty.Length != (nuint)0 || empty.Data[0] != '\0') return 3;
                try { String bad = Base64Text::Decode("TR==", 4); return 4; } catch (Exception* error) { }
                return 0;
            }
            """, text: true);
    }
}
