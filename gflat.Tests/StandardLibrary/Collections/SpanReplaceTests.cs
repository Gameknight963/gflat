namespace gflat.Tests.Std.Collections;

public sealed partial class SpanReplaceTests
{
    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Collections/EqualityComparer.gf", "core/Collections/default_equality.gf",
            "core/Collections/span_replace.gf"))
        .Append(new SourceFile("span-replace.gf", code));

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("SPAN_REPLACE.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllSmallReplacementValuesMatchDotNet(int optimization)
    {
        var packet = new List<byte>();
        int scenarios = 0;
        int combinations = 1;
        for (int length = 0; length <= 4; length++)
        {
            for (int pattern = 0; pattern < combinations; pattern++)
            for (int oldValue = 0; oldValue <= 3; oldValue++)
            for (int newValue = 0; newValue <= 3; newValue++)
            {
                int[] input = [99, 99, 99, 99];
                int remaining = pattern;
                for (int i = 0; i < length; i++) { input[i] = remaining % 3; remaining /= 3; }
                int[] expected = (int[])input.Clone();
                expected.AsSpan(0, length).Replace(oldValue, newValue);
                int[] copied = Enumerable.Repeat(99, 6).ToArray();
                ((ReadOnlySpan<int>)input.AsSpan(0, length)).Replace(copied, oldValue, newValue);
                packet.AddRange(new[] { length, oldValue, newValue }.Select(value => (byte)value));
                packet.AddRange(input.Select(value => (byte)value));
                packet.AddRange(expected.Select(value => (byte)value));
                packet.AddRange(copied.Select(value => (byte)value));
                scenarios++;
            }
            combinations *= 3;
        }
        string hex = Convert.ToHexString(packet.ToArray());
        var result = CompilerTestHelper.Run(Sources($$"""
            using Standard;
            using Standard::Memory;
            using Standard::Collections;

            int calls = 0;
            bool Equal(immutable(int)* left, immutable(int)* right) { calls++; return *left == *right; }
            int Nibble(char value)
            {
                if (value <= '9') return (int)value - (int)'0';
                return (int)value - (int)'A' + 10;
            }
            int Read(readonly(char)* input, ref nuint position)
            {
                int value = Nibble(input[position]) * 16 + Nibble(input[position + 1]);
                position += 2;
                return value;
            }
            int main()
            {
                readonly(char)* packet = "{{hex}}";
                nuint position = 0;
                for (int scenario = 0; scenario < {{scenarios}}; scenario++)
                {
                    nuint length = (nuint)Read(packet, ref position);
                    int oldValue = Read(packet, ref position);
                    int newValue = Read(packet, ref position);
                    int[4] original = default(int[4]);
                    int[4] changed = default(int[4]);
                    for (nuint i = 0; i < 4; i++) { original[i] = Read(packet, ref position); changed[i] = original[i]; }
                    Span<int> values = new(&changed[0], length);
                    values.Replace(oldValue, newValue);
                    for (nuint i = 0; i < 4; i++) if (changed[i] != Read(packet, ref position)) return 1;
                    int[6] output = [99, 99, 99, 99, 99, 99];
                    Span<int> destination = new(&output[0], 6);
                    Span<int> input = new(&original[0], length);
                    Replace<int>(input.AsImmutable(), destination, oldValue, newValue);
                    input.AsImmutable().Replace(destination, oldValue, newValue);
                    calls = 0;
                    input.AsImmutable().Replace(destination, oldValue, newValue, Equal);
                    if (calls != (int)length) return 2;
                    for (nuint i = 0; i < 6; i++) if (output[i] != Read(packet, ref position)) return 3;
                    calls = 0;
                    values.Replace(oldValue, newValue, Equal);
                    if (calls != (int)length) return 4;
                }
                int[3] aliases = [1, 2, 1];
                Span<int> aliasView = new(&aliases[0], 3);
                Replace<int>(aliasView, aliases[0], aliases[1]);
                if (aliases[0] != 2 || aliases[1] != 2 || aliases[2] != 2) return 5;
                if (position != {{hex.Length}}) return 6;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Span replace oracle exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverlapAndDestinationValidationMatchDotNetBeforeCallbacks(int optimization)
    {
        var packet = new List<byte>();
        int scenarios = 0;
        for (int start = 0; start <= 8; start++)
        for (int count = 0; count <= 8 - start; count++)
        for (int destination = 0; destination <= 8; destination++)
        {
            int[] values = Enumerable.Range(0, 8).Select(i => i % 3).ToArray();
            bool valid = true;
            try { ((ReadOnlySpan<int>)values.AsSpan(start, count)).Replace(values.AsSpan(destination), 1, 9); }
            catch (ArgumentException) { valid = false; }
            packet.AddRange(new[] { start, count, destination, valid ? 1 : 0 }.Select(value => (byte)value));
            packet.AddRange(values.Select(value => (byte)value));
            scenarios++;
        }
        string hex = Convert.ToHexString(packet.ToArray());
        var result = CompilerTestHelper.Run(Sources($$"""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;

            int calls = 0;
            bool Equal(immutable(int)* left, immutable(int)* right) { calls++; return *left == *right; }
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
                    nuint start = Read(packet, ref position);
                    nuint count = Read(packet, ref position);
                    nuint destination = Read(packet, ref position);
                    bool valid = Read(packet, ref position) != 0;
                    int[8] buffer = [0, 1, 2, 0, 1, 2, 0, 1];
                    Span<int> values = new(&buffer[0], 8);
                    calls = 0;
                    bool caught = false;
                    try { Replace<int>(values.AsImmutable().Slice(start, count), values.Slice(destination), 1, 9, Equal); }
                    catch (Exception* error) { caught = true; }
                    if (caught == valid) return 1;
                    if (!valid && calls != 0) return 2;
                    if (valid && calls != (int)count) return 3;
                    for (nuint i = 0; i < 8; i++) if (buffer[i] != (int)Read(packet, ref position)) return 4;
                }
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Span replace overlap exit {result.ExitCode}: {result.StandardError}");
    }
}
