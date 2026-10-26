namespace gflat.Tests;

public sealed class StandardHexTests
{
    private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void Run(string program, bool text = false)
    {
        var names = new List<string> { "core/Encoding/Hex.gf" };
        if (text) names.AddRange(["core/Encoding/HexText.gf", "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing/primitive_hashing.gf", "core/String.gf", "core/ByteSearch.gf", "core/Formatting.gf"]);
        var files = names.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", program));
        var result = CompilerTestHelper.Run(files);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryByteMatchesDotNetAndRoundTripsInPlace(bool uppercase)
    {
        byte[] input = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        string expected = Convert.ToHexString(input);
        if (!uppercase) expected = expected.ToLowerInvariant();
        Run($$"""
            using std; using std::Encoding;
            int main() {
                char[256] input; char[513] output;
                for (int i = 0; i < 256; i++) input[i] = (char)i;
                output[512] = '?';
                nuint written = 99;
                if (!Hex::TryEncode(input, 256, output, 512, ref written, {{uppercase.ToString().ToLowerInvariant()}}) || written != (nuint)512) return 1;
                for (nuint i = 0; i < written; i++) if (output[i] != {{Literal(expected)}}[i]) return 2;
                if (output[512] != '?') return 3;
                if (!Hex::TryDecode(output, 512, output, 256, ref written) || written != (nuint)256) return 4;
                for (int i = 0; i < 256; i++) if ((byte)output[i] != (byte)i) return 5;
                return 0;
            }
            """);
    }

    [Theory]
    [InlineData("F")]
    [InlineData("0xFF")]
    [InlineData("AA BB")]
    [InlineData("AAGG")]
    [InlineData("FF--")]
    public void MalformedInputDoesNotWriteOutput(string malformed)
    {
        Run($$"""
            using std; using std::Encoding;
            int main() {
                char[8] output;
                for (int i = 0; i < 8; i++) output[i] = '?';
                nuint written = 99;
                if (Hex::TryGetDecodedLength({{Literal(malformed)}}, {{malformed.Length}}, ref written) || written != (nuint)0) return 1;
                if (Hex::TryDecode({{Literal(malformed)}}, {{malformed.Length}}, output, 8, ref written) || written != (nuint)0) return 2;
                for (int i = 0; i < 8; i++) if (output[i] != '?') return 3;
                return 0;
            }
            """);
    }

    [Fact]
    public void EmptyMixedCaseCapacityAndLengthOverflowAreHandled()
    {
        Run("""
            using std; using std::Encoding;
            int main() {
                char[8] output;
                for (int i = 0; i < 8; i++) output[i] = '?';
                nuint written = 99;
                if (!Hex::TryEncode("", 0, output, 0, ref written) || written != (nuint)0 || output[0] != '?') return 1;
                if (!Hex::TryDecode("", 0, output, 0, ref written) || written != (nuint)0 || output[0] != '?') return 2;
                if (Hex::TryEncode("a", 1, output, 1, ref written) || written != (nuint)0 || output[0] != '?') return 3;
                if (Hex::TryDecode("ff", 2, output, 0, ref written) || written != (nuint)0 || output[0] != '?') return 4;
                if (!Hex::TryDecode("aAbB00fF", 8, output, 4, ref written)) return 5;
                if ((byte)output[0] != (byte)170 || (byte)output[1] != (byte)187 || output[2] != '\0' || (byte)output[3] != (byte)255) return 6;
                nuint maximum = (nuint)0 - 1;
                if (Hex::TryGetEncodedLength(maximum, ref written) || written != (nuint)0) return 7;
                if (!Hex::TryGetEncodedLength(maximum / 2, ref written) || written != maximum - 1) return 8;
                return 0;
            }
            """);
    }

    [Fact]
    public void OwningConveniencesRetainBinaryZerosAndThrowOnInvalidText()
    {
        Run("""
            using std; using std::Encoding;
            int main() {
                String text = HexText::Encode("A\0B", 3);
                if (text.Length != (nuint)6 || text.Data[0] != '4' || text.Data[1] != '1' || text.Data[2] != '0' || text.Data[3] != '0' || text.Data[4] != '4' || text.Data[5] != '2') return 1;
                String bytes = HexText::Decode(text.Data, text.Length);
                if (bytes.Length != (nuint)3 || bytes.Data[0] != 'A' || bytes.Data[1] != '\0' || bytes.Data[2] != 'B') return 2;
                char high = (char)255;
                String lower = HexText::Encode(&high, 1, false);
                if (lower.Length != (nuint)2 || lower.Data[0] != 'f' || lower.Data[1] != 'f') return 3;
                try { String invalid = HexText::Decode("0x", 2); return 4; } catch (Exception* error) { }
                return 0;
            }
            """, text: true);
    }
}
