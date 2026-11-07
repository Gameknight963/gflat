using System.Text;

namespace gflat.Tests.Std.Codecs;

public sealed class PercentEncodingSpanTests
{
    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Encoding/PercentEncoding.gf", "core/Encoding/percent_encoding_span.gf"))
            .Append(new SourceFile("percent-spans.gf", code)), optimization);
        Assert.True(result.ExitCode == 0, $"Percent spans exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("PERCENT_ENCODING_SPANS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Utf8MatchesDotNetAndRawBytesPreserveGuardsAtEveryCapacity(int optimization)
    {
        var rows = new List<(byte[] Bytes, string Encoded)>();
        foreach (string text in new[] { "", "hello world", "AZaz09-._~", "a+b/c?x=y", "caf\u00e9 / \u6c34 \U0001f642", "a\0b" })
            rows.Add((Encoding.UTF8.GetBytes(text), Uri.EscapeDataString(text)));
        const string unreserved = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
        string Encode(byte[] bytes) => string.Concat(bytes.Select(value => unreserved.Contains((char)value)
            ? ((char)value).ToString() : "%" + value.ToString("X2")));
        foreach (int count in Enumerable.Range(0, 17).Concat(new[] { 31, 63, 127, 255, 256 }))
        {
            byte[] bytes = Enumerable.Range(0, count).Select(value => (byte)value).ToArray();
            rows.Add((bytes, Encode(bytes)));
        }
        var random = new Random(774541);
        for (int i = 0; i < 48; i++)
        {
            byte[] bytes = new byte[random.Next(33)];
            random.NextBytes(bytes);
            rows.Add((bytes, Encode(bytes)));
        }
        string checks = string.Join("\n", rows.Select(row => $$"""
            {
                byte[{{Math.Max(1, row.Bytes.Length)}}] input = [{{string.Join(",", row.Bytes.DefaultIfEmpty((byte)0).Select(value => "(byte)" + value))}}];
                if (!Check(&input[0], {{row.Bytes.Length}}, "{{row.Encoded}}", {{row.Encoded.Length}})) return 1;
            }
            """));
        Run(Support + """
            bool Check(readonly(byte)* input, nuint count, readonly(char)* expected, nuint length) nothrow
            {
                var bytes = new Span<readonly(byte)>(input, count);
                var chars = new Span<readonly(char)>(input.reinterpret<readonly(char)*>(), count);
                nuint required = 999;
                if (!bytes.TryGetPercentEncodedLength(ref required) || required != length) return false;
                if (!chars.TryGetPercentEncodedLength(ref required) || required != length) return false;
                for (int form = 0; form < 2; form++)
                {
                    for (nuint capacity = 0; capacity <= length + 1; capacity++)
                    {
                        char[772] output;
                        for (nuint i = 0; i < 772; i++) output[i] = '#';
                        nuint written = 999;
                        bool success = false;
                        if (form == 0) success = bytes.TryEncodePercent(new Span<char>(&output[1], capacity), ref written);
                        else success = Standard::Encoding::TryEncodePercent(chars, new Span<char>(&output[1], capacity), ref written);
                        if (success != (capacity >= length)) return false;
                        nuint actual = 0;
                        if (success) actual = length;
                        if (written != actual) return false;
                        for (nuint i = 0; i < 772; i++)
                        {
                            char value = '#';
                            if (i >= 1 && i <= actual) value = expected[i - 1];
                            if (output[i] != value) return false;
                        }
                    }
                    var text = new Span<readonly(char)>(expected, length);
                    if (!text.TryGetPercentDecodedLength(ref required) || required != count) return false;
                    for (nuint capacity = 0; capacity <= count + 1; capacity++)
                    {
                        byte[260] output;
                        for (nuint i = 0; i < 260; i++) output[i] = (byte)90;
                        nuint written = 999;
                        bool success = false;
                        if (form == 0) success = text.TryDecodePercent(new Span<byte>(&output[1], capacity), ref written);
                        else success = Standard::Encoding::TryDecodePercent(text, new Span<char>((&output[1]).reinterpret<char*>(), capacity), ref written);
                        if (success != (capacity >= count)) return false;
                        nuint actual = 0;
                        if (success) actual = count;
                        if (written != actual) return false;
                        for (nuint i = 0; i < 260; i++)
                        {
                            byte value = (byte)90;
                            if (i >= 1 && i <= actual) value = input[i - 1];
                            if (output[i] != value) return false;
                        }
                    }
                    char[768] inplace;
                    for (nuint i = 0; i < length; i++) inplace[i] = expected[i];
                    var encoded = new Span<readonly(char)>(&inplace[0], length);
                    nuint written = 999;
                    bool success = false;
                    if (form == 0) success = encoded.TryDecodePercent(new Span<char>(&inplace[0], count), ref written);
                    else success = encoded.TryDecodePercent(new Span<byte>((&inplace[0]).reinterpret<byte*>(), count), ref written);
                    if (!success || written != count) return false;
                    for (nuint i = 0; i < count; i++) if ((byte)inplace[i] != input[i]) return false;
                }
                return true;
            }
            int main()
            {
            """ + checks + "return attempts; }", optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MalformedEscapesAndEmptyDefaultsKeepCountsAndBuffersAtomic(int optimization)
        => Run(Support + """
            bool Check() nothrow
            {
                nuint written = 999;
                if (!new Span<readonly(byte)>().TryGetPercentEncodedLength(ref written) || written != 0) return false;
                if (!new Span<readonly(char)>().TryGetPercentEncodedLength(ref written) || written != 0) return false;
                if (!new Span<readonly(char)>().TryGetPercentDecodedLength(ref written) || written != 0) return false;
                if (!new Span<readonly(byte)>().TryEncodePercent(new Span<char>(), ref written) || written != 0) return false;
                if (!new Span<readonly(char)>().TryEncodePercent(new Span<char>(), ref written) || written != 0) return false;
                if (!new Span<readonly(char)>().TryDecodePercent(new Span<byte>(), ref written) || written != 0) return false;
                if (!new Span<readonly(char)>().TryDecodePercent(new Span<char>(), ref written) || written != 0) return false;
                for (int value = 0; value < 256; value++)
                {
                    if ((value >= '0' && value <= '9') || (value >= 'A' && value <= 'F') || (value >= 'a' && value <= 'f')) continue;
                    for (int position = 1; position <= 2; position++)
                    {
                        char[3] input = ['%', '0', '0'];
                        input[(nuint)position] = (char)value;
                        char[8] output;
                        for (nuint i = 0; i < 8; i++) output[i] = '#';
                        var text = new Span<readonly(char)>(&input[0], 3);
                        if (text.TryGetPercentDecodedLength(ref written) || written != 0) return false;
                        if (text.TryDecodePercent(new Span<char>(&output[0], 8), ref written) || written != 0) return false;
                        if (text.TryDecodePercent(new Span<byte>((&output[0]).reinterpret<byte*>(), 8), ref written) || written != 0) return false;
                        for (nuint i = 0; i < 8; i++) if (output[i] != '#') return false;
                    }
                }
                char[3] text = ['+', '%', '2'];
                byte[3] output;
                if (new Span<readonly(char)>(&text[0], 3).TryDecodePercent(new Span<byte>(&output[0], 3), ref written) || written != 0) return false;
                if (!new Span<readonly(char)>(&text[0], 1).TryDecodePercent(new Span<byte>(&output[0], 3), ref written) || written != 1 || output[0] != (byte)'+') return false;
                char[2] raw = ['\0', '+'];
                if (!new Span<readonly(char)>(&raw[0], 2).TryDecodePercent(new Span<byte>(&output[0], 3), ref written) || written != 2 || output[0] != (byte)0 || output[1] != (byte)'+') return false;
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
