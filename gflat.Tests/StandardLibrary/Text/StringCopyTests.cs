namespace gflat.Tests.Std.Text;

public sealed class StringCopyTests
{
    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Memory/bytes.gf", "core/Text/string_span.gf", "core/Text/string_copy.gf"))
        .Append(new SourceFile("string-copy.gf", code));

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("STRING_COPY.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RangeCopiesAndInvalidBoundsMatchDotNetWithoutAllocatingOrChangingTheSource(int optimization)
    {
        char[] input = ['\0', (char)1, (char)127, (char)128, (char)255, 'A', '\0', 'Z'];
        string source = new(input);
        var packet = new List<byte>();
        int scenarios = 0;
        for (int from = 0; from <= 9; from++)
        for (int to = 0; to <= 11; to++)
        for (int count = 0; count <= 9; count++)
        {
            char[] expected = Enumerable.Repeat((char)200, 10).ToArray();
            bool valid = true;
            try { source.CopyTo(from, expected, to, count); }
            catch (ArgumentException) { valid = false; }
            packet.AddRange(new[] { from, to, count, valid ? 1 : 0 }.Select(value => (byte)value));
            packet.AddRange(expected.Select(value => (byte)value));
            scenarios++;
        }
        string hex = Convert.ToHexString(packet.ToArray());
        var result = CompilerTestHelper.Run(Sources($$"""
            using Standard;
            using Standard::Memory;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny) return null;
                    return malloc(bytes);
                }
                public replace void Free(void* storage) nothrow => free(storage);
            }
            int Nibble(char value)
            {
                if (value <= '9') return (int)value - (int)'0';
                return (int)value - (int)'A' + 10;
            }
            nuint Read(readonly(char)* input, ref nuint position)
            {
                int value = Nibble(input[position]) * 16 + Nibble(input[position + 1]);
                position += 2;
                return (nuint)value;
            }
            bool Copy(immutable(String)* source, Span<char> output) nothrow => source.TryCopyTo(output);
            int main()
            {
                char[8] input = ['\0', (char)1, (char)127, (char)128, (char)255, 'A', '\0', 'Z'];
                String text = String::From(&input[0], 8);
                readonly(char)* address = text.Data;
                nuint capacity = text.Capacity;
                readonly(char)* packet = "{{hex}}";
                nuint position = 0;
                for (int scenario = 0; scenario < {{scenarios}}; scenario++)
                {
                    nuint from = Read(packet, ref position);
                    nuint to = Read(packet, ref position);
                    nuint count = Read(packet, ref position);
                    bool valid = Read(packet, ref position) != 0;
                    char[10] output = default(char[10]);
                    for (nuint i = 0; i < 10; i++) output[i] = (char)200;
                    Span<char> destination = new(&output[0], 10);
                    int before = attempts;
                    deny = valid;
                    bool caught = false;
                    try { text.CopyTo(from, destination, to, count); }
                    catch (Exception* error) { caught = true; }
                    deny = false;
                    if (caught == valid || (valid && attempts != before)) return 1;
                    for (nuint i = 0; i < 10; i++) if ((nuint)(byte)output[i] != Read(packet, ref position)) return 2;
                    if (text.Data != address || text.Length != 8 || text.Capacity != capacity || text.Data[8] != '\0') return 3;
                    for (nuint i = 0; i < 8; i++) if (text[i] != input[i]) return 4;
                }
                char[10] output = ['.', '.', '.', '.', '.', '.', '.', '.', '.', '.'];
                Span<char> destination = new(&output[0], 10);
                int before = attempts;
                deny = true;
                text.CopyTo(destination);
                CopyTo(&text, destination);
                if (!Copy(&text, destination) || !TryCopyTo(&text, destination)) return 5;
                if (text.TryCopyTo(destination.Slice(3)) || output[8] != '.' || output[9] != '.' || attempts != before) return 6;
                deny = false;
                CopyTo(&text, 1, destination, 2, 3);
                if ((byte)output[2] != (byte)1 || (byte)output[3] != (byte)127 || (byte)output[4] != (byte)128) return 7;
                nuint maximum = (nuint)0 - (nuint)1;
                try { text.CopyTo(maximum, destination, 0, 0); return 8; }
                catch (Exception* error) { }
                try { text.CopyTo(0, destination, maximum, 0); return 9; }
                catch (Exception* error) { }
                try { text.CopyTo(0, destination, 0, maximum); return 10; }
                catch (Exception* error) { }
                for (nuint length = 0; length <= 8; length++)
                {
                    String fragment = String::From(&input[0], length);
                    for (nuint available = 0; available <= 10; available++)
                    {
                        for (nuint i = 0; i < 10; i++) output[i] = (char)200;
                        Span<char> target = new(&output[0], available);
                        before = attempts;
                        deny = true;
                        bool copied = fragment.TryCopyTo(target);
                        if (copied != (available >= length) || attempts != before) return 13;
                        for (nuint i = 0; i < 10; i++)
                        {
                            char expected = (char)200;
                            if (copied && i < length) expected = input[i];
                            if (output[i] != expected) return 14;
                            output[i] = (char)200;
                        }
                        deny = available >= length;
                        before = attempts;
                        bool caught = false;
                        try { fragment.CopyTo(target); }
                        catch (Exception* error) { caught = true; }
                        deny = false;
                        if (caught == (available >= length) || (!caught && attempts != before)) return 15;
                        for (nuint i = 0; i < 10; i++)
                        {
                            char expected = (char)200;
                            if (!caught && i < length) expected = input[i];
                            if (output[i] != expected) return 16;
                        }
                    }
                }
                String empty = new();
                Span<char> nowhere = new();
                deny = true;
                before = attempts;
                empty.CopyTo(nowhere);
                empty.CopyTo(0, nowhere, 0, 0);
                if (!empty.TryCopyTo(nowhere) || attempts != before) return 11;
                deny = false;
                if (position != {{hex.Length}}) return 12;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"String copy oracle exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverlappingSelfRangesMatchArrayCopyAndPreserveLengthAndTerminator(int optimization)
    {
        var packet = new List<byte>();
        int scenarios = 0;
        for (int count = 0; count <= 8; count++)
        for (int from = 0; from <= 8 - count; from++)
        for (int to = 0; to <= 8 - count; to++)
        {
            byte[] expected = [0, 1, 127, 128, 255, 65, 0, 90];
            Array.Copy(expected, from, expected, to, count);
            packet.AddRange(new[] { from, to, count }.Select(value => (byte)value));
            packet.AddRange(expected);
            scenarios++;
        }
        string hex = Convert.ToHexString(packet.ToArray());
        var result = CompilerTestHelper.Run(Sources($$"""
            using Standard::Text;

            int Nibble(char value)
            {
                if (value <= '9') return (int)value - (int)'0';
                return (int)value - (int)'A' + 10;
            }
            nuint Read(readonly(char)* input, ref nuint position)
            {
                int value = Nibble(input[position]) * 16 + Nibble(input[position + 1]);
                position += 2;
                return (nuint)value;
            }
            int main()
            {
                readonly(char)* packet = "{{hex}}";
                nuint position = 0;
                for (int scenario = 0; scenario < {{scenarios}}; scenario++)
                {
                    nuint from = Read(packet, ref position);
                    nuint to = Read(packet, ref position);
                    nuint count = Read(packet, ref position);
                    char[8] bytes = ['\0', (char)1, (char)127, (char)128, (char)255, 'A', '\0', 'Z'];
                    String text = String::From(&bytes[0], 8);
                    readonly(char)* original = text.Data;
                    nuint capacity = text.Capacity;
                    text.CopyTo(from, text.AsSpan(), to, count);
                    for (nuint i = 0; i < 8; i++) if ((nuint)(byte)text[i] != Read(packet, ref position)) return 1;
                    if (text.Length != 8 || text.Capacity != capacity || text.Data != original || text.Data[8] != '\0') return 2;
                }
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"String copy overlap exit {result.ExitCode}: {result.StandardError}");
    }
}
