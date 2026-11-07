using System.Globalization;
using System.Text;
using Integer = System.Numerics.BigInteger;

namespace gflat.Tests.Std.Numerics;

public sealed class BigIntegerBytesTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("BIG_INTEGER_BYTES.md", 0);

    private static IEnumerable<byte[]> Cases()
    {
        yield return [];
        for (int value = 0; value < 256; value++) yield return [(byte)value];
        for (int length = 1; length <= 64; length++)
        {
            yield return new byte[length];
            yield return Enumerable.Repeat((byte)255, length).ToArray();
            byte[] sign = new byte[length]; sign[^1] = 128; yield return sign;
            byte[] padded = Enumerable.Repeat((byte)255, length).ToArray(); padded[^1] = 127; yield return padded;
        }
        var random = new Random(797221);
        for (int i = 0; i < 128; i++)
        {
            byte[] data = new byte[random.Next(1, 129)];
            random.NextBytes(data);
            yield return data;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryObservedAllocationFailurePreservesSourcesDestinationsAndOwnedStorage(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Numerics/BigInteger.gf"))
            .Append(new SourceFile("big-integer-bytes-failure.gf", """
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Numerics;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int attempts = 0;
            int failAt = -1;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    int attempt = attempts;
                    attempts++;
                    if (attempt == failAt) return null;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            int main()
            {
                byte[64] input;
                for (nuint i = 0; i < 64; i++) input[i] = (byte)(i * 3 + 1);
                {
                    BigInteger value = BigInteger::FromBytes(&input[0], 64, true);
                    BigInteger original = value;
                    input[0] = (byte)255;
                    if (!value.Equals(&original)) return 1;
                    input[0] = (byte)1;
                    Vector<byte> destination = new();
                    destination.Add((byte)99);
                    int allocationCount = 0;
                    for (int format = 0; format < 4; format++)
                    {
                        attempts = 0;
                        {
                            Vector<byte> measured = value.ToBytes(format >= 2, format % 2 != 0);
                            allocationCount = attempts;
                        }
                        for (int failure = 0; failure < allocationCount; failure++)
                        {
                            attempts = 0; failAt = failure;
                            int beforeLive = live;
                            bool caught = false;
                            try { destination = value.ToBytes(format >= 2, format % 2 != 0); }
                            catch (Exception* error) { caught = true; }
                            failAt = -1;
                            if (!caught || live != beforeLive || destination.Count != 1 || destination[0] != (byte)99) return 2;
                            if (!value.Equals(&original)) return 3;
                        }
                    }
                    BigInteger target = new(123l);
                    for (int format = 0; format < 4; format++)
                    {
                        attempts = 0;
                        {
                            BigInteger measured = BigInteger::FromBytes(&input[0], 64, format >= 2, format % 2 != 0);
                            allocationCount = attempts;
                        }
                        for (int failure = 0; failure < allocationCount; failure++)
                        {
                            attempts = 0; failAt = failure;
                            int beforeLive = live;
                            bool caught = false;
                            try { target = BigInteger::FromBytes(&input[0], 64, format >= 2, format % 2 != 0); }
                            catch (Exception* error) { caught = true; }
                            failAt = -1;
                            if (!caught || live != beforeLive || target.ToLong() != 123l) return 4;
                            for (nuint i = 0; i < 64; i++) if (input[i] != (byte)(i * 3 + 1)) return 5;
                        }
                    }
                    attempts = 0; failAt = 0;
                    BigInteger zero = BigInteger::FromBytes(new Span<readonly(byte)>());
                    failAt = -1;
                    if (attempts != 0 || zero.Sign != 0) return 6;
                }
                if (live != 0) return 7;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"Byte conversion cleanup exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImportExportAndMinimalSignedRepresentationsMatchDotNet(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_bigint_bytes_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (byte[] data in Cases())
            for (int flags = 0; flags < 4; flags++)
            {
                bool unsigned = flags >= 2, bigEndian = flags % 2 != 0;
                var value = new Integer(data, unsigned, bigEndian);
                byte[] text = Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));
                foreach (int header in new[] { data.Length, unsigned ? 1 : 0, bigEndian ? 1 : 0, text.Length }) writer.Write(header);
                writer.Write(data); writer.Write(text);
                for (int output = 0; output < 4; output++)
                {
                    try
                    {
                        byte[] encoded = value.ToByteArray(output >= 2, output % 2 != 0);
                        writer.Write(encoded.Length); writer.Write(encoded);
                    }
                    catch (OverflowException) { writer.Write(-1); }
                }
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Numerics/BigInteger.gf"))
                .Append(new SourceFile("big-integer-bytes.gf", $$"""
                using Standard;
                using Standard::Collections;
                using Standard::Memory;
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int[4] header;
                    byte[128] input;
                    char[512] text;
                    char[512] actual;
                    byte[129] expected;
                    while (true)
                    {
                        nuint received = fread(&header[0], 1, 16, file!);
                        if (received == 0) break;
                        if (received != 16) return 2;
                        if (fread(&input[0], 1, (nuint)header[0], file!) != (nuint)header[0]) return 3;
                        if (fread(&text[0], 1, (nuint)header[3], file!) != (nuint)header[3]) return 4;
                        BigInteger value = BigInteger::FromBytes(&input[0], (nuint)header[0], header[1] != 0, header[2] != 0);
                        Span<readonly(byte)> view = new(&input[0], (nuint)header[0]);
                        if (header[0] == 0) view = new Span<readonly(byte)>();
                        BigInteger spanValue = BigInteger::FromBytes(view, header[1] != 0, header[2] != 0);
                        if (!value.Equals(&spanValue)) return 5;
                        nuint written = 0;
                        if (!value.TryFormat(&actual[0], 512, ref written) || written != (nuint)header[3]) return 6;
                        for (nuint i = 0; i < written; i++) if (actual[i] != text[i]) return 7;
                        for (int form = 0; form < 4; form++)
                        {
                            int count = 0;
                            if (fread(&count, 1, 4, file!) != 4) return 8;
                            if (count >= 0 && fread(&expected[0], 1, (nuint)count, file!) != (nuint)count) return 9;
                            Vector<byte> encoded = new();
                            encoded.Add((byte)123);
                            bool caught = false;
                            try { encoded = value.ToBytes(form >= 2, form % 2 != 0); }
                            catch (Exception* error) { caught = true; }
                            if (count < 0)
                            {
                                if (!caught || encoded.Count != 1 || encoded[0] != (byte)123) return 10;
                                continue;
                            }
                            if (caught || encoded.Count != (nuint)count) return 11;
                            for (nuint i = 0; i < encoded.Count; i++) if (encoded[i] != expected[i]) return 12;
                            BigInteger roundTrip = BigInteger::FromBytes(&encoded[0], encoded.Count, form >= 2, form % 2 != 0);
                            if (!value.Equals(&roundTrip)) return 13;
                            encoded[0] = (byte)0;
                            if (!value.Equals(&spanValue)) return 14;
                        }
                    }
                    if (ferror(file!) != 0) return 15;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"BigInteger bytes exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
