namespace gflat.Tests.Std.Codecs;

public sealed class Base64SpanTests
{
    private static void Run(string code, bool url, int optimization)
    {
        string codec = url ? "Base64Url" : "Base64";
        string adapter = url ? "base64_url_span.gf" : "base64_span.gf";
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load($"core/Encoding/{codec}.gf", $"core/Encoding/{adapter}"))
            .Append(new SourceFile("base64-spans.gf", code.Replace("CODEC", codec))), optimization);
        Assert.True(result.ExitCode == 0, $"{codec} spans exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("BASE64_SPANS.md", 0);

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public void EveryBytePrefixMatchesDotNetWithGuardedBuffersAndInPlaceDecode(int optimization, bool url)
    {
        byte[] input = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        string[] rows = Enumerable.Range(0, 257).Select(count =>
        {
            string text = Convert.ToBase64String(input.AsSpan(0, count));
            return url ? text.TrimEnd('=').Replace('+', '-').Replace('/', '_') : text;
        }).ToArray();
        Run(Support + $$"""
            bool Check() nothrow
            {
                readonly(char)* all = "{{string.Concat(rows)}}";
                nuint[257] lengths = [{{string.Join(",", rows.Select(row => row.Length))}}];
                nuint offset = 0;
                byte[256] input;
                for (nuint i = 0; i < 256; i++) input[i] = (byte)i;
                for (nuint count = 0; count <= 256; count++)
                {
                    readonly(char)* expected = all + offset;
                    nuint length = lengths[count];
                    nuint required = 999;
                    if (!CODEC::TryGetEncodedLength(count, ref required) || required != length) return false;
                    for (int form = 0; form < 2; form++)
                    {
                        nuint[4] capacities = [0, length, length + 1, 0];
                        if (length != 0) capacities[3] = length - 1;
                        for (nuint sample = 0; sample < 4; sample++)
                        {
                            char[348] output;
                            for (nuint i = 0; i < 348; i++) output[i] = '#';
                            nuint written = 999;
                            var destination = new Span<char>(&output[1], capacities[sample]);
                            bool success = false;
                            if (form == 0) success = new Span<readonly(byte)>(&input[0], count).TryEncodeCODEC(destination, ref written);
                            else success = Standard::Encoding::TryEncodeCODEC(new Span<readonly(char)>((&input[0]).reinterpret<readonly(char)*>(), count), destination, ref written);
                            bool wanted = capacities[sample] >= length;
                            if (success != wanted) return false;
                            nuint actual = 0;
                            if (wanted) actual = length;
                            if (written != actual) return false;
                            for (nuint i = 0; i < 348; i++)
                            {
                                char value = '#';
                                if (i >= 1 && i <= actual) value = expected[i - 1];
                                if (output[i] != value) return false;
                            }
                        }
                        var text = new Span<readonly(char)>(expected, length);
                        nuint decoded = 999;
                        if (!text.TryGetCODECDecodedLength(ref decoded) || decoded != count) return false;
                        capacities[0] = 0; capacities[1] = count; capacities[2] = count + 1; capacities[3] = 0;
                        if (count != 0) capacities[3] = count - 1;
                        for (nuint sample = 0; sample < 4; sample++)
                        {
                            byte[260] output;
                            for (nuint i = 0; i < 260; i++) output[i] = (byte)90;
                            nuint written = 999;
                            bool success = false;
                            if (form == 0) success = text.TryDecodeCODEC(new Span<byte>(&output[1], capacities[sample]), ref written);
                            else success = Standard::Encoding::TryDecodeCODEC(text, new Span<char>((&output[1]).reinterpret<char*>(), capacities[sample]), ref written);
                            bool wanted = capacities[sample] >= count;
                            if (success != wanted) return false;
                            nuint actual = 0;
                            if (wanted) actual = count;
                            if (written != actual) return false;
                            for (nuint i = 0; i < 260; i++)
                            {
                                byte value = (byte)90;
                                if (i >= 1 && i <= actual) value = input[i - 1];
                                if (output[i] != value) return false;
                            }
                        }
                        char[344] inplace;
                        for (nuint i = 0; i < length; i++) inplace[i] = expected[i];
                        nuint written = 999;
                        var encoded = new Span<readonly(char)>(&inplace[0], length);
                        bool success = false;
                        if (form == 0) success = encoded.TryDecodeCODEC(new Span<char>(&inplace[0], count), ref written);
                        else success = encoded.TryDecodeCODEC(new Span<byte>((&inplace[0]).reinterpret<byte*>(), count), ref written);
                        if (!success || written != count) return false;
                        for (nuint i = 0; i < count; i++) if ((byte)inplace[i] != input[i]) return false;
                    }
                    offset += length;
                }
                return attempts == 0;
            }
            int main() { if (!Check()) return 1; return 0; }
            """, url, optimization);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public void EmptyDefaultsAndMalformedTextRetainAtomicFailure(int optimization, bool url)
    {
        string[] malformed = url
            ? ["A", "AA=", "AAA=", "AAAA=", "AB", "AAB", "+AAA", "/AAA", "AA A", "AA\nA", "\0AAA"]
            : ["A", "AAA", "AA=A", "A===", "AAAA=", "AB==", "AAB=", "AA A", "AA\nA", "-AAA", "_AAA", "\0AAA"];
        string checks = string.Join("\n", malformed.Select(text =>
        {
            string bytes = string.Join(",", text.Select(value => "(char)" + (int)value));
            return $$"""
                {
                    char[{{text.Length}}] bytes = [{{bytes}}];
                    if (!Invalid(new Span<readonly(char)>(&bytes[0], {{text.Length}}))) return 2;
                }
                """;
        }));
        Run(Support + """
            bool Invalid(Span<readonly(char)> text) nothrow
            {
                char[16] output;
                for (nuint i = 0; i < 16; i++) output[i] = '#';
                nuint written = 999;
                if (text.TryGetCODECDecodedLength(ref written) || written != 0) return false;
                if (text.TryDecodeCODEC(new Span<char>(&output[0], 16), ref written) || written != 0) return false;
                if (text.TryDecodeCODEC(new Span<byte>((&output[0]).reinterpret<byte*>(), 16), ref written) || written != 0) return false;
                for (nuint i = 0; i < 16; i++) if (output[i] != '#') return false;
                return true;
            }
            int main()
            {
                nuint written = 999;
                if (!new Span<readonly(byte)>().TryEncodeCODEC(new Span<char>(), ref written) || written != 0) return 1;
                if (!new Span<readonly(char)>().TryEncodeCODEC(new Span<char>(), ref written) || written != 0) return 1;
                if (!new Span<readonly(char)>().TryDecodeCODEC(new Span<char>(), ref written) || written != 0) return 1;
                if (!new Span<readonly(char)>().TryDecodeCODEC(new Span<byte>(), ref written) || written != 0) return 1;
                if (!new Span<readonly(char)>().TryGetCODECDecodedLength(ref written) || written != 0) return 1;
            """ + checks + "return attempts; }", url, optimization);
    }

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
