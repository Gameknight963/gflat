using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class StringRepeatTests
{
    private const string Allocator = """
        using Standard; using Standard::Text;
        extern void*? malloc(nuint bytes); extern void free(void* storage);
        int attempts = 0; int allocations = 0; int releases = 0;
        bool deny = false; bool failNext = false; nuint failedSize = 0;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                attempts++;
                if (failNext) { failNext = false; failedSize = bytes; return null; }
                if (deny) return null;
                void*? result = malloc(bytes); if (result != null) allocations++; return result;
            }
            public replace void Free(void* storage) nothrow { releases++; free(storage); }
        }
        int Digit(char value) nothrow { if (value <= '9') return value - '0'; return value - 'A' + 10; }
        int ReadByte(readonly(char)* data, ref nuint at) nothrow {
            int value = (Digit(data[at]) << 4) | Digit(data[at + 1]); at += 2; return value;
        }
        nuint ReadCount(readonly(char)* data, ref nuint at) nothrow {
            int low = ReadByte(data, ref at); return (nuint)(low | (ReadByte(data, ref at) << 8));
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedBinaryRepetitionMatchesIndependentByteOracleAndUsesOneResultAllocation(int optimization)
    {
        var cases = new List<(byte[] Pattern, int Times)> { ([], 0), ([], 1), ([], 33),
            ([65, 0, 255], 0), ([65, 0, 255], 1), ([65, 0, 255], 2), ([65, 0, 255], 3),
            ([65, 0, 255], 7), ([65, 0, 255], 31), ([65, 0, 255], 32), ([65, 0, 255], 33),
            ([0], 65), ([255], 17), ([195, 169, 0, 240, 159, 146, 161], 9) };
        var random = new Random(613892);
        for (int row = 0; row < 24; row++)
        {
            byte[] pattern = new byte[random.Next(0, 24)]; random.NextBytes(pattern);
            cases.Add((pattern, random.Next(0, 34)));
        }
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
            foreach (var row in cases)
            {
                byte[] expected = Enumerable.Repeat(row.Pattern, row.Times).SelectMany(pattern => pattern).ToArray();
                writer.Write((byte)row.Pattern.Length); writer.Write((ushort)row.Times); writer.Write((ushort)expected.Length);
                writer.Write(row.Pattern); writer.Write(expected);
            }
        string hex = Convert.ToHexString(bytes.ToArray());
        Run(Allocator + $$"""
            int main() {
                readonly(char)* packet = "{{hex}}"; nuint at = 0;
                for (int row = 0; row < {{cases.Count}}; row++) {
                    {
                        nuint length = (nuint)ReadByte(packet, ref at);
                        nuint times = ReadCount(packet, ref at); nuint expectedLength = ReadCount(packet, ref at);
                        char[32] input; input[0] = (char)17; input[length + 1] = (char)33;
                        for (nuint i = 0; i < length; i++) input[i + 1] = (char)ReadByte(packet, ref at);
                        String source = String::From(&input[1], length); source.Reserve(length + 19);
                        readonly(char)* identity = source.Data; nuint capacity = source.Capacity;
                        immutable(String)* view = &source; int before = attempts;
                        String result = view.Repeat(times);
                        int expectedAllocations = 0; if (length != 0 && times != 0) expectedAllocations = 1;
                        if (attempts != before + expectedAllocations || result.Length != expectedLength || result.Data[result.Length] != '\0') return 1;
                        for (nuint i = 0; i < expectedLength; i++)
                            if (result.Data[i] != (char)ReadByte(packet, ref at)) return 2;
                        if (source.Data != identity || source.Capacity != capacity || source.Length != length || source.Data[length] != '\0') return 3;
                        for (nuint i = 0; i < length; i++) if (source.Data[i] != input[i + 1]) return 4;
                        if (input[0] != (char)17 || input[length + 1] != (char)33) return 5;
                        if (expectedLength != 0) {
                            if (result.Data == source.Data) return 6;
                            char original = source.Data[0]; result[0] = (char)((byte)original ^ (byte)255);
                            if (source.Data[0] != original) return 7;
                            char changed = result.Data[0]; source[0] = (char)((byte)original ^ (byte)127);
                            if (result.Data[0] != changed) return 8;
                        }
                    }
                    if (allocations != releases) return 9;
                }
                if (at != {{hex.Length}}) return 10;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyAndZeroCasesAllocateNothingAndNonemptyResultOutlivesSource(int optimization)
        => Run(Allocator + """
            String Produce() {
                char[3] bytes = ['a', '\0', (char)255];
                String temporary = String::From(bytes, 3);
                return temporary.Repeat(5);
            }
            int main() {
                {
                    String empty = new(); String reserved = new(); reserved.Reserve(64);
                    String source = s"payload"; readonly(char)* identity = source.Data;
                    nuint capacity = reserved.Capacity; int before = attempts;
                    nuint maximum = (nuint)0 - (nuint)1; deny = true;
                    String a = empty.Repeat(maximum); String b = reserved.Repeat(maximum); String c = source.Repeat(0);
                    deny = false;
                    if (attempts != before || a.Length != 0 || b.Length != 0 || c.Length != 0) return 1;
                    if (a.Data[0] != '\0' || b.Data[0] != '\0' || c.Data[0] != '\0') return 2;
                    if (reserved.Capacity != capacity || reserved.Length != 0 || source.Data != identity || !source.Equals("payload")) return 3;
                    String result = Produce();
                    if (result.Length != 15 || result.Data[15] != '\0') return 4;
                    for (nuint i = 0; i < 15; i++) {
                        char expected = 'a'; if (i % 3 == 1) expected = '\0'; else if (i % 3 == 2) expected = (char)255;
                        if (result.Data[i] != expected) return 5;
                    }
                    String literalResult = s"ab".Repeat(3);
                    if (!literalResult.Equals("ababab")) return 6;
                }
                if (allocations != releases) return 7;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverflowAndDeniedResultAllocationPreserveReceiverAndAssignmentTarget(int optimization)
        => Run(Allocator + """
            int main() {
                {
                    char[3] bytes = ['a', '\0', (char)255]; String source = String::From(bytes, 3); source.Reserve(47);
                    String target = s"keep"; readonly(char)* sourceAddress = source.Data; readonly(char)* targetAddress = target.Data;
                    nuint targetCapacity = target.Capacity; immutable(String)* view = &source;
                    nuint maximum = (nuint)0 - (nuint)1;
                    nuint[2] invalid = [maximum, (maximum - 1) / 3 + 1];
                    for (nuint i = 0; i < 2; i++) {
                        bool caught = false;
                        try { target = view.Repeat(invalid[i]); }
                        catch (ref Exception error) { caught = error.GetCode() == 1 && CString::Equals(error.GetMessage(), "String length overflow"); }
                        if (!caught || source.Data != sourceAddress || source.Length != 3 || source.Capacity != 47 || source.Data[3] != '\0') return 1;
                        if (target.Data != targetAddress || target.Capacity != targetCapacity || !target.Equals("keep")) return 2;
                        for (nuint at = 0; at < 3; at++) if (source.Data[at] != bytes[at]) return 3;
                    }
                    bool caught = false; failNext = true;
                    try { target = view.Repeat(5); }
                    catch (ref BadAllocException error) { caught = CString::Equals(error.GetMessage(), "String allocation failed"); }
                    if (!caught || failNext || failedSize != 16 || source.Data != sourceAddress || source.Capacity != 47 || source.Length != 3) return 4;
                    if (target.Data != targetAddress || target.Capacity != targetCapacity || !target.Equals("keep")) return 5;
                    caught = false; failNext = true;
                    try { source = view.Repeat(2); }
                    catch (ref BadAllocException error) { caught = true; }
                    if (!caught || source.Data != sourceAddress || source.Length != 3 || source.Capacity != 47 || source.Data[3] != '\0') return 6;
                    for (nuint at = 0; at < 3; at++) if (source.Data[at] != bytes[at]) return 7;
                    // A valid boundary request reaches the allocator; no giant allocation is attempted.
                    String single = s"x"; readonly(char)* singleAddress = single.Data; caught = false; failNext = true;
                    try { target = single.Repeat(maximum - 1); }
                    catch (ref BadAllocException error) { caught = true; }
                    if (!caught || failNext || failedSize != maximum || single.Data != singleAddress || !single.Equals("x")) return 8;
                    if (target.Data != targetAddress || target.Capacity != targetCapacity || !target.Equals("keep")) return 9;
                }
                if (allocations != releases) return 10;
                return 0;
            }
            """, optimization);

    private static void Run(string source, int optimization)
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("string-repeat.gf", source));
        string ir = Compiler.Emit(files, options: new() { InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
