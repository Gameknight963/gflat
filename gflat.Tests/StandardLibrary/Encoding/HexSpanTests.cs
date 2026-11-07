namespace gflat.Tests.Std.Codecs;

public sealed class HexSpanTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Encoding/Hex.gf", "core/Encoding/hex_span.gf"))
            .Append(new SourceFile("hex-spans.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"Hex spans exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("HEX_SPANS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllBytePrefixesMatchDotNetInBothCasesAndGuardAllDestinationForms(int optimization)
    {
        string upper = Convert.ToHexString(Enumerable.Range(0, 256).Select(value => (byte)value).ToArray());
        Run(Support + $$"""
            bool Check() nothrow
            {
                readonly(char)* upper = "{{upper}}";
                readonly(char)* lower = "{{upper.ToLowerInvariant()}}";
                byte[256] input;
                for (nuint i = 0; i < 256; i++) input[i] = (byte)i;
                for (nuint count = 0; count <= 256; count++)
                for (int casing = 0; casing < 2; casing++)
                for (int form = 0; form < 2; form++)
                {
                    readonly(char)* expected = upper;
                    if (casing != 0) expected = lower;
                    nuint[4] capacities = [0, count * 2, count * 2 + 1, 0];
                    if (count != 0) capacities[3] = count * 2 - 1;
                    for (nuint sample = 0; sample < 4; sample++)
                    {
                        char[516] output;
                        for (nuint i = 0; i < 516; i++) output[i] = '#';
                        nuint written = 999;
                        var destination = new Span<char>(&output[1], capacities[sample]);
                        bool success = false;
                        if (form == 0)
                            success = new Span<readonly(byte)>(&input[0], count).TryEncodeHex(destination, ref written, casing == 0);
                        else
                            success = Standard::Encoding::TryEncodeHex(new Span<readonly(char)>((&input[0]).reinterpret<readonly(char)*>(), count), destination, ref written, casing == 0);
                        bool wanted = capacities[sample] >= count * 2;
                        if (success != wanted) return false;
                        nuint length = 0;
                        if (wanted) length = count * 2;
                        if (written != length) return false;
                        for (nuint i = 0; i < 516; i++)
                        {
                            char value = '#';
                            if (i >= 1 && i <= length) value = expected[i - 1];
                            if (output[i] != value) return false;
                        }
                    }
                    var text = new Span<readonly(char)>(expected, count * 2);
                    nuint decoded = 999;
                    if (!text.TryGetHexDecodedLength(ref decoded) || decoded != count) return false;
                    capacities[0] = 0;
                    capacities[1] = count;
                    capacities[2] = count + 1;
                    capacities[3] = 0;
                    if (count != 0) capacities[3] = count - 1;
                    for (nuint sample = 0; sample < 4; sample++)
                    {
                        byte[260] output;
                        for (nuint i = 0; i < 260; i++) output[i] = (byte)90;
                        nuint written = 999;
                        bool success = false;
                        if (form == 0) success = text.TryDecodeHex(new Span<byte>(&output[1], capacities[sample]), ref written);
                        else success = Standard::Encoding::TryDecodeHex(text, new Span<char>((&output[1]).reinterpret<char*>(), capacities[sample]), ref written);
                        bool wanted = capacities[sample] >= count;
                        if (success != wanted) return false;
                        nuint length = 0;
                        if (wanted) length = count;
                        if (written != length) return false;
                        for (nuint i = 0; i < 260; i++)
                        {
                            byte value = (byte)90;
                            if (i >= 1 && i <= length) value = input[i - 1];
                            if (output[i] != value) return false;
                        }
                    }
                    char[512] inplace;
                    for (nuint i = 0; i < count * 2; i++) inplace[i] = expected[i];
                    nuint written = 999;
                    var encoded = new Span<readonly(char)>(&inplace[0], count * 2);
                    bool success = false;
                    if (form == 0) success = encoded.TryDecodeHex(new Span<char>(&inplace[0], count), ref written);
                    else success = encoded.TryDecodeHex(new Span<byte>((&inplace[0]).reinterpret<byte*>(), count), ref written);
                    if (!success || written != count) return false;
                    for (nuint i = 0; i < count; i++) if ((byte)inplace[i] != input[i]) return false;
                }
                return attempts == 0;
            }
            int main() { if (!Check()) return 1; return 0; }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyDefaultsAndEveryInvalidBytePreserveDestinationWithoutAllocation(int optimization)
        => Run(Support + """
            bool Check() nothrow
            {
                nuint written = 999;
                if (!new Span<readonly(byte)>().TryEncodeHex(new Span<char>(), ref written) || written != 0) return false;
                if (!new Span<readonly(char)>().TryEncodeHex(new Span<char>(), ref written) || written != 0) return false;
                if (!new Span<readonly(char)>().TryDecodeHex(new Span<byte>(), ref written) || written != 0) return false;
                if (!new Span<readonly(char)>().TryDecodeHex(new Span<char>(), ref written) || written != 0) return false;
                if (!new Span<readonly(char)>().TryGetHexDecodedLength(ref written) || written != 0) return false;
                for (int value = 0; value < 256; value++)
                {
                    if ((value >= '0' && value <= '9') || (value >= 'A' && value <= 'F') || (value >= 'a' && value <= 'f')) continue;
                    for (nuint position = 0; position < 4; position++)
                    {
                        char[4] input = ['0', '0', 'F', 'f'];
                        input[position] = (char)value;
                        char[8] output;
                        for (nuint i = 0; i < 8; i++) output[i] = '#';
                        var text = new Span<readonly(char)>(&input[0], 4);
                        if (text.TryGetHexDecodedLength(ref written) || written != 0) return false;
                        if (text.TryDecodeHex(new Span<char>(&output[0], 8), ref written) || written != 0) return false;
                        if (text.TryDecodeHex(new Span<byte>((&output[0]).reinterpret<byte*>(), 8), ref written) || written != 0) return false;
                        for (nuint i = 0; i < 8; i++) if (output[i] != '#') return false;
                    }
                }
                char[5] odd = ['A', 'A', 'A', 'A', 'A'];
                for (nuint count = 1; count <= 5; count += 2)
                {
                    var text = new Span<readonly(char)>(&odd[0], count);
                    if (text.TryGetHexDecodedLength(ref written) || written != 0) return false;
                }
                return attempts == 0;
            }
            int main() { if (!Check()) return 1; return 0; }
            """, optimization);

    private const string Support = """
        using Standard;
        using Standard::Encoding;
        using Standard::Memory;

        int attempts = 0;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes) { attempts++; return null; }
            public replace void Free(void* storage) nothrow { }
        }
        """;
}
