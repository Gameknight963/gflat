namespace gflat.Tests.Std.Text;

public sealed class HexBytePointerTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("HEX_BYTE_BUFFERS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllBytesAndShortCapacitiesMatchDotNetAndInPlaceDecodingPreservesGuards(int optimization)
    {
        string expected = Convert.ToHexString(Enumerable.Range(0, 256).Select(value => (byte)value).ToArray());
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Encoding/Hex.gf", "core/Encoding/HexText.gf"))
            .Append(new SourceFile("hex-byte-buffers.gf", $$"""
                using Standard;
                using Standard::Encoding;
                using Standard::Text;

                int main()
                {
                    byte[256] data;
                    for (nuint i = 0; i < 256; i++) data[i] = (byte)i;
                    readonly(char)* oracle = "{{expected}}";
                    for (nuint length = 0; length <= 256; length++)
                    {
                        char[514] text;
                        byte[258] decoded;
                        for (nuint i = 0; i < 514; i++) text[i] = '!';
                        for (nuint i = 0; i < 258; i++) decoded[i] = (byte)165;
                        nuint written = 99;
                        if (length != 0)
                        {
                            if (Hex::TryEncode(&data[0], length, &text[1], length * 2 - 1, ref written) || written != 0) return 1;
                            for (nuint i = 0; i < 514; i++) if (text[i] != '!') return 2;
                        }
                        if (!Hex::TryEncode(&data[0], length, &text[1], length * 2, ref written) || written != length * 2) return 3;
                        if (text[0] != '!' || text[length * 2 + 1] != '!') return 4;
                        for (nuint i = 0; i < length * 2; i++) if (text[i + 1] != oracle[i]) return 5;
                        if (length != 0)
                        {
                            if (Hex::TryDecode(&text[1], length * 2, &decoded[1], length - 1, ref written) || written != 0) return 6;
                            for (nuint i = 0; i < 258; i++) if (decoded[i] != (byte)165) return 7;
                        }
                        if (!Hex::TryDecode(&text[1], length * 2, &decoded[1], length, ref written) || written != length) return 8;
                        if (decoded[0] != (byte)165 || decoded[length + 1] != (byte)165) return 9;
                        for (nuint i = 0; i < length; i++) if (decoded[i + 1] != (byte)i) return 10;
                        if (!Hex::TryDecode(&text[1], length * 2, (&text[1]).reinterpret<byte*>(), length, ref written)) return 11;
                        for (nuint i = 0; i < length; i++) if ((byte)text[i + 1] != (byte)i) return 12;
                        String owned = HexText::Encode(&data[0], length);
                        if (!owned.Equals(oracle, length * 2)) return 13;
                    }
                    byte[2] old = [(byte)77, (byte)88];
                    nuint written = 99;
                    if (Hex::TryDecode("0G", 2, &old[0], 2, ref written) || written != 0 || old[0] != (byte)77) return 14;
                    if (Hex::TryDecode("0", 1, &old[0], 2, ref written) || written != 0 || old[1] != (byte)88) return 15;
                    byte value = (byte)171;
                    String lower = HexText::Encode(&value, 1, false);
                    if (!lower.Equals("ab")) return 16;
                    return 0;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
