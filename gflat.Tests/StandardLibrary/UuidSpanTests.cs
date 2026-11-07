using System.Buffers.Binary;
using System.Text;

namespace gflat.Tests.Std;

public sealed class UuidSpanTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Uuid.gf"))
            .Append(new SourceFile("uuid-spans.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"UUID span exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("UUID_SPANS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExactTextAndNetworkBytesMatchGuidWithGuardedDestinations(int optimization)
    {
        var random = new Random(774120);
        var checks = new StringBuilder();
        for (int row = 0; row < 48; row++)
        {
            byte[] bytes = new byte[16];
            if (row != 0) random.NextBytes(bytes);
            var id = new Guid(bytes, bigEndian: true);
            ulong high = BinaryPrimitives.ReadUInt64BigEndian(bytes);
            ulong low = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8));
            checks.AppendLine($$"""
                {
                    Uuid id = new({{high}}ul, {{low}}ul);
                    byte[16] bytes = [{{string.Join(",", bytes.Select(value => "(byte)" + value))}}];
                    if (CheckBytes(&id, &bytes[0])) return 1;
                """);
            foreach (char format in "NDBPndbp")
            {
                string text = id.ToString(format.ToString());
                checks.AppendLine($$"""
                    if (CheckText(&id, "{{text}}", '{{format}}')) return 2;
                    {
                        Uuid parsed = Uuid::ParseExact(new Span<readonly(char)>("{{text}}", {{text.Length}}), '{{format}}');
                        if (!parsed.Equals(&id)) return 3;
                    }
                    """);
            }
            checks.AppendLine("}");
        }
        Run(Support + """
            bool CheckText(immutable(Uuid)* id, readonly(char)* expected, char format) nothrow
            {
                nuint count = 0;
                while (expected[count] != '\0') count++;
                char[40] input;
                for (nuint i = 0; i < count; i++) input[i + 1] = expected[i];
                input[count + 1] = '#';
                var text = new Span<readonly(char)>(&input[1], count);
                Uuid parsed = new();
                if (!Uuid::TryParseExact(text, format, &parsed) || !parsed.Equals(id)) return true;
                if (format == 'D' || format == 'd')
                {
                    if (!Uuid::TryParse(text, &parsed) || !parsed.Equals(id)) return true;
                }
                for (nuint capacity = 0; capacity <= count + 1; capacity++)
                {
                    char[42] output;
                    for (nuint i = 0; i < 42; i++) output[i] = '#';
                    nuint written = 999;
                    bool success = id.TryFormat(new Span<char>(&output[1], capacity), ref written, format);
                    if (success != (capacity >= count)) return true;
                    nuint wanted = 0;
                    if (success) wanted = count;
                    if (written != wanted) return true;
                    for (nuint i = 0; i < 42; i++)
                    {
                        char value = '#';
                        if (success && i >= 1 && i <= count) value = expected[i - 1];
                        if (output[i] != value) return true;
                    }
                }
                for (nuint i = 0; i < count; i++)
                {
                    char saved = input[i + 1];
                    input[i + 1] = '\0';
                    if (Uuid::TryParseExact(text, format, &parsed) || !parsed.IsNil) return true;
                    input[i + 1] = saved;
                }
                for (nuint i = 0; i < count; i++)
                {
                    if (input[i + 1] >= 'a' && input[i + 1] <= 'f') input[i + 1] = (char)(input[i + 1] - 'a' + 'A');
                }
                if (!Uuid::TryParseExact(text, format, &parsed) || !parsed.Equals(id)) return true;
                if (Uuid::TryParseExact(text, 'X', &parsed) || !parsed.IsNil) return true;
                return false;
            }

            bool CheckBytes(immutable(Uuid)* id, readonly(byte)* expected) nothrow
            {
                byte[18] input;
                for (nuint i = 0; i < 16; i++) input[i + 1] = expected[i];
                Uuid parsed = new();
                if (!Uuid::TryFromBytes(new Span<readonly(byte)>(&input[1], 16), &parsed) || !parsed.Equals(id)) return true;
                for (nuint capacity = 0; capacity <= 17; capacity++)
                {
                    byte[20] output;
                    for (nuint i = 0; i < 20; i++) output[i] = (byte)90;
                    nuint written = 999;
                    bool success = id.TryWriteBytes(new Span<byte>(&output[1], capacity), ref written);
                    if (success != (capacity >= 16)) return true;
                    nuint wanted = 0;
                    if (success) wanted = 16;
                    if (written != wanted) return true;
                    for (nuint i = 0; i < 20; i++)
                    {
                        byte value = (byte)90;
                        if (success && i >= 1 && i <= 16) value = expected[i - 1];
                        if (output[i] != value) return true;
                    }
                }
                return false;
            }
            """ + $$"""
            int main()
            {
                {{checks}}
                return attempts;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyAndInvalidSpansPreservePreflightAndThrowingParseContracts(int optimization)
        => Run(Support + """
            int main()
            {
                Uuid id = new(1ul, 2ul);
                Uuid parsed = id;
                nuint written = 999;
                if (id.TryFormat(new Span<char>(), ref written) || written != 0) return 1;
                if (id.TryFormat(new Span<char>(), ref written, 'D') || written != 0) return 2;
                if (id.TryWriteBytes(new Span<byte>(), ref written) || written != 0) return 3;
                if (Uuid::TryParse(new Span<readonly(char)>(), &parsed) || !parsed.IsNil) return 4;
                if (Uuid::TryParseExact(new Span<readonly(char)>(), 'D', &parsed) || !parsed.IsNil) return 5;
                for (nuint count = 0; count <= 20; count++)
                {
                    if (count == 16) continue;
                    var input = new Span<readonly(byte)>(((nuint)1).reinterpret<readonly(byte)*>(), count);
                    parsed = id;
                    if (Uuid::TryFromBytes(input, &parsed) || !parsed.IsNil) return 6;
                    bool caught = false;
                    try { parsed = Uuid::FromBytes(input); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || !parsed.IsNil) return 7;
                }
                for (int form = 0; form < 2; form++)
                {
                    bool caught = false;
                    try
                    {
                        if (form == 0) parsed = Uuid::Parse(new Span<readonly(char)>());
                        else parsed = Uuid::ParseExact(new Span<readonly(char)>(), 'D');
                    }
                    catch (Exception* error) { caught = true; }
                    if (!caught) return 8;
                }
                readonly(char)* text = "00112233-4455-6677-8899-aabbccddeeff";
                parsed = Uuid::Parse(new Span<readonly(char)>(text, 36));
                byte[16] bytes;
                if (!parsed.TryWriteBytes(new Span<byte>(&bytes[0], 16), ref written)) return 9;
                Uuid roundtrip = Uuid::FromBytes(new Span<readonly(byte)>(&bytes[0], 16));
                if (!parsed.Equals(&roundtrip)) return 10;
                char[36] output;
                if (!roundtrip.TryFormat(new Span<char>(&output[0], 36), ref written) || written != 36) return 11;
                for (nuint i = 0; i < 36; i++) if (output[i] != text[i]) return 12;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ByteSpansRetainBackedOverlapSupportWithoutAllocation(int optimization)
        => Run(Support + """
            int main()
            {
                for (nuint offset = 0; offset <= 32; offset += 4)
                for (nuint resultIndex = 0; resultIndex < 3; resultIndex++)
                {
                    Uuid[4] aligned;
                    byte* data = (&aligned[0]).reinterpret<byte*>();
                    for (nuint i = 0; i < 64; i++) data[i] = (byte)90;
                    for (nuint i = 0; i < 16; i++) data[offset + i] = (byte)(i * 17);
                    byte[64] before;
                    for (nuint i = 0; i < 64; i++) before[i] = data[i];
                    if (!Uuid::TryFromBytes(new Span<readonly(byte)>(data + offset, 16), &aligned[resultIndex])) return 1;
                    if (aligned[resultIndex].High != 0x0011223344556677ul || aligned[resultIndex].Low != 0x8899aabbccddeefful) return 2;
                    for (nuint i = 0; i < 64; i++)
                        if ((i < resultIndex * 16 || i >= resultIndex * 16 + 16) && data[i] != before[i]) return 3;
                }
                for (nuint offset = 0; offset <= 32; offset += 4)
                {
                    Uuid[4] aligned;
                    byte* data = (&aligned[0]).reinterpret<byte*>();
                    for (nuint i = 0; i < 64; i++) data[i] = (byte)90;
                    aligned[1] = new Uuid(0x0011223344556677ul, 0x8899aabbccddeefful);
                    byte[64] before;
                    for (nuint i = 0; i < 64; i++) before[i] = data[i];
                    nuint written = 0;
                    if (!aligned[1].TryWriteBytes(new Span<byte>(data + offset, 16), ref written) || written != 16) return 4;
                    for (nuint i = 0; i < 64; i++)
                    {
                        byte wanted = before[i];
                        if (i >= offset && i < offset + 16) wanted = (byte)((i - offset) * 17);
                        if (data[i] != wanted) return 5;
                    }
                }
                return attempts;
            }
            """, optimization);

    private const string Support = """
        using Standard;
        using Standard::Memory;

        int attempts = 0;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes) { attempts++; return null; }
            public replace void Free(void* storage) nothrow { }
        }
        """;
}
