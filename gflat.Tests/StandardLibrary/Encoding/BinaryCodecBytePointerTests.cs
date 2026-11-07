using System.Text;

namespace gflat.Tests.Std.Codecs;

public sealed class BinaryCodecBytePointerTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("BASE64_BYTE_BUFFERS.md", 0);

    [Fact]
    public void AdditionalGuideCompiles() => StandardLibraryGuideTests.CheckProgram("ENCODED_BYTE_BUFFERS.md", 0);

    private static string Base32(ReadOnlySpan<byte> data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new StringBuilder();
        int pending = 0, bits = 0;
        foreach (byte value in data)
        {
            pending = (pending << 8) | value;
            bits += 8;
            while (bits >= 5) { bits -= 5; result.Append(alphabet[(pending >> bits) & 31]); }
        }
        if (bits != 0) result.Append(alphabet[(pending << (5 - bits)) & 31]);
        while (result.Length % 8 != 0) result.Append('=');
        return result.ToString();
    }

    private static string Percent(ReadOnlySpan<byte> data)
    {
        var result = new StringBuilder();
        foreach (byte value in data)
        {
            if (value is >= 65 and <= 90 or >= 97 and <= 122 or >= 48 and <= 57 or 45 or 46 or 95 or 126)
                result.Append((char)value);
            else result.Append(Uri.HexEscape((char)value));
        }
        return result.ToString();
    }

    [Theory]
    [InlineData("Base32", 0)]
    [InlineData("Base32", 2)]
    [InlineData("PercentEncoding", 0)]
    [InlineData("PercentEncoding", 2)]
    [InlineData("Base64", 0)]
    [InlineData("Base64", 2)]
    [InlineData("Base64Url", 0)]
    [InlineData("Base64Url", 2)]
    public void ByteBuffersMatchIndependentOraclesAndRespectCapacityAndInPlaceContracts(string codec, int optimization)
    {
        var program = new StringBuilder($$"""
            using Standard;
            using Standard::Encoding;
            using Standard::Text;

            int Check(readonly(byte)* data, nuint count, readonly(char)* oracle, nuint encodedLength)
            {
                char[770] encoded;
                byte[258] decoded;
                for (nuint i = 0; i < 770; i++) encoded[i] = '!';
                for (nuint i = 0; i < 258; i++) decoded[i] = (byte)165;
                nuint written = 99;
                nuint required = 0;
                if (!{{codec}}::TryGetEncodedLength({{(codec == "PercentEncoding" ? "data, " : "")}}count, ref required) || required != encodedLength) return 14;

                if (encodedLength != 0)
                {
                    if ({{codec}}::TryEncode(data, count, &encoded[1], encodedLength - 1, ref written) || written != 0) return 1;
                    for (nuint i = 0; i < 770; i++) if (encoded[i] != '!') return 2;
                }
                if (!{{codec}}::TryEncode(data, count, &encoded[1], encodedLength, ref written) || written != encodedLength) return 3;
                for (nuint i = 0; i < encodedLength; i++) if (encoded[i + 1] != oracle[i]) return 4;
                if (encoded[0] != '!' || encoded[encodedLength + 1] != '!') return 5;
                if (count != 0)
                {
                    if ({{codec}}::TryDecode(&encoded[1], encodedLength, &decoded[1], count - 1, ref written) || written != 0) return 6;
                    for (nuint i = 0; i < 258; i++) if (decoded[i] != (byte)165) return 7;
                }
                if (!{{codec}}::TryDecode(&encoded[1], encodedLength, &decoded[1], count, ref written) || written != count) return 8;
                for (nuint i = 0; i < count; i++) if (decoded[i + 1] != data[i]) return 9;
                if (decoded[0] != (byte)165 || decoded[count + 1] != (byte)165) return 10;
                if (!{{codec}}::TryDecode(&encoded[1], encodedLength, (&encoded[1]).reinterpret<byte*>(), count, ref written)) return 11;
                for (nuint i = 0; i < count; i++) if ((byte)encoded[i + 1] != data[i]) return 12;
                String owned = {{codec}}Text::Encode(data, count);
                if (!owned.Equals(oracle, encodedLength)) return 13;
                return 0;
            }
            int main()
            {
                byte[256] data;
                for (nuint i = 0; i < 256; i++) data[i] = (byte)i;
            """);
        byte[] all = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        for (int count = 0; count <= 256; count++)
        {
            string expected = Convert.ToBase64String(all, 0, count);
            if (codec == "Base32") expected = Base32(all.AsSpan(0, count));
            if (codec == "PercentEncoding") expected = Percent(all.AsSpan(0, count));
            if (codec == "Base64Url") expected = expected.TrimEnd('=').Replace('+', '-').Replace('/', '_');
            program.AppendLine($"if (Check(&data[0], {count}, \"{expected}\", {expected.Length}) != 0) return 20;");
        }
        program.AppendLine($$"""
                byte[2] output = [(byte)77, (byte)88];
                nuint written = 99;
                if ({{codec}}::TryDecode("%%%%", 4, &output[0], 2, ref written) || written != 0) return 21;
                if (output[0] != (byte)77 || output[1] != (byte)88) return 22;
                return 0;
            }
            """);
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load($"core/Encoding/{codec}.gf", $"core/Encoding/{codec}Text.gf"))
            .Append(new SourceFile("binary-codec-byte-pointers.gf", program.ToString()));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
