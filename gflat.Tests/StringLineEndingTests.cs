using System.Text;

namespace gflat.Tests;

public sealed class StringLineEndingTests
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
        nuint ReadByte(readonly(char)* data, ref nuint at) nothrow {
            int value = (Digit(data[at]) << 4) | Digit(data[at + 1]); at += 2; return (nuint)value;
        }
        nuint ReadCount(readonly(char)* data, ref nuint at) nothrow {
            nuint low = ReadByte(data, ref at); return low | (ReadByte(data, ref at) << 8);
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MixedLineEndingsMatchIndependentByteOracleAndPreserveInsertedBytes(int optimization)
    {
        string[] sources = ["", "a", "\r", "\n", "\r\n", "\r\r\n", "\n\r", "\r\n\r",
            "\n\n\r\r\r\n", "a\rb\nc\r\nd\r", "\0\r\n\u00ff\n\0\r", "\u0085\u000b\u000c\u00e2\u0080\u00a8"];
        string[] replacements = ["", "\n", "\r\n", "<END>", "\r\r\n", "\0\u00ff\n"];
        var cases = sources.SelectMany(source => replacements.Select(replacement => (Source: source, Replacement: replacement))).ToList();
        var random = new Random(1703201);
        const string alphabet = "ab\r\n\0\u00ff\u0085";
        string Sample(int length) => new(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
        for (int sample = 0; sample < 96; sample++) cases.Add((Sample(random.Next(0, 100)), Sample(random.Next(0, 8))));

        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, Encoding.UTF8, true))
            foreach (var row in cases)
            {
                string expected = row.Source.Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace("\r", "\n", StringComparison.Ordinal)
                    .Replace("\n", row.Replacement, StringComparison.Ordinal);
                writer.Write((ushort)row.Source.Length);
                writer.Write((ushort)row.Replacement.Length);
                writer.Write((ushort)expected.Length);
                writer.Write(Encoding.Latin1.GetBytes(row.Source));
                writer.Write(Encoding.Latin1.GetBytes(row.Replacement));
                writer.Write(Encoding.Latin1.GetBytes(expected));
            }
        string hex = Convert.ToHexString(packet.ToArray());
        Run(Allocator + $$"""
            int main() {
                readonly(char)* packet = "{{hex}}"; nuint at = 0;
                for (int row = 0; row < {{cases.Count}}; row++) {
                    {
                        nuint sourceLength = ReadCount(packet, ref at);
                        nuint replacementLength = ReadCount(packet, ref at);
                        nuint expectedLength = ReadCount(packet, ref at);
                        char[128] source; char[32] replacement;
                        source[0] = '!'; source[sourceLength + 1] = '?';
                        replacement[0] = '!'; replacement[replacementLength + 1] = '?';
                        bool hasEnding = false;
                        for (nuint i = 0; i < sourceLength; i++) {
                            char value = (char)ReadByte(packet, ref at); source[i + 1] = value;
                            if (value == '\r' || value == '\n') hasEnding = true;
                        }
                        for (nuint i = 0; i < replacementLength; i++) replacement[i + 1] = (char)ReadByte(packet, ref at);
                        String text = String::From(&source[1], sourceLength); text.Reserve(sourceLength + 17);
                        readonly(char)* identity = text.Data; nuint capacity = text.Capacity; int before = attempts;
                        text.ReplaceLineEndings(&replacement[1], replacementLength);
                        int expectedAllocations = 0; if (hasEnding && expectedLength != 0) expectedAllocations = 1;
                        if (attempts != before + expectedAllocations || text.Length != expectedLength || text.Data[text.Length] != '\0') return 1;
                        if (!hasEnding && (text.Data != identity || text.Capacity != capacity)) return 2;
                        for (nuint i = 0; i < expectedLength; i++) if (text.Data[i] != (char)ReadByte(packet, ref at)) return 3;
                        if (source[0] != '!' || source[sourceLength + 1] != '?' || replacement[0] != '!' || replacement[replacementLength + 1] != '?') return 4;
                    }
                    if (allocations != releases) return 5;
                }
                if (at != {{hex.Length}}) return 6;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DefaultCStringAndAliasedReplacementsUseOriginalStorageUntilCommit(int optimization)
        => Run(Allocator + """
            int main() {
                {
                    String defaults = s"a\rb\r\nc\n";
                    defaults.ReplaceLineEndings();
                    if (!defaults.Equals("a\nb\nc\n")) return 10;
                    String custom = s"a\rb\r\nc\n";
                    custom.ReplaceLineEndings("<>\0ignored");
                    if (!custom.Equals("a<>b<>c<>")) return 11;
                    String aliased = s"A\r\nB\nC\r"; aliased.Reserve(100);
                    aliased.ReplaceLineEndings(aliased.Data + 3, 3);
                    if (!aliased.Equals("AB\nCBB\nCCB\nC")) return 12;
                    String self = s"\r\n";
                    self.ReplaceLineEndings(self.Data, self.Length);
                    if (!self.Equals("\r\n")) return 13;
                    String spare = s"a\rb\0XY"; spare.Reserve(64); spare.Resize(3);
                    spare.ReplaceLineEndings(spare.Data + 4, 2);
                    if (!spare.Equals("aXYb")) return 14;
                    String erase = s"\r\n\r\n\r\n\r";
                    int before = attempts; deny = true;
                    erase.ReplaceLineEndings("");
                    deny = false;
                    if (attempts != before || erase.Length != 0 || erase.Capacity != 0 || erase.Data[0] != '\0') return 15;
                    String stable = s"plain\0text"; stable.Reserve(100);
                    readonly(char)* identity = stable.Data; nuint capacity = stable.Capacity;
                    before = attempts; deny = true;
                    stable.ReplaceLineEndings("\r\n");
                    deny = false;
                    if (attempts != before || stable.Data != identity || stable.Capacity != capacity || stable.Length != 10) return 16;
                    String empty = new(); before = attempts; deny = true;
                    empty.ReplaceLineEndings();
                    deny = false;
                    if (attempts != before || empty.Length != 0 || empty.Capacity != 0) return 17;
                }
                if (allocations != releases) return 18;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedAllocationAndCheckedLengthOverflowPreserveOriginalBytesAndCapacity(int optimization)
        => Run(Allocator + """
            int main() {
                {
                    String text = s"a\r\nb\nc\r"; text.Reserve(100);
                    readonly(char)* identity = text.Data; nuint capacity = text.Capacity;
                    bool caught = false; failNext = true;
                    try { text.ReplaceLineEndings(text.Data + 3, 3); }
                    catch (ref BadAllocException error) { caught = true; }
                    if (!caught || failNext || failedSize != 13 || text.Data != identity || text.Capacity != capacity || !text.Equals("a\r\nb\nc\r")) return 20;
                    nuint maximum = (nuint)0 - (nuint)1;
                    nuint[2] invalid = [maximum, (maximum - 4) / 3 + 1];
                    for (nuint i = 0; i < 2; i++) {
                        caught = false;
                        try { text.ReplaceLineEndings("x", invalid[i]); }
                        catch (ref Exception error) { caught = CString::Equals(error.GetMessage(), "String length overflow"); }
                        if (!caught || text.Data != identity || text.Capacity != capacity || !text.Equals("a\r\nb\nc\r")) return 21;
                    }
                    String boundary = s"\r"; readonly(char)* boundaryIdentity = boundary.Data;
                    caught = false; failNext = true;
                    try { boundary.ReplaceLineEndings("x", maximum - 1); }
                    catch (ref BadAllocException error) { caught = true; }
                    if (!caught || failNext || failedSize != maximum || boundary.Data != boundaryIdentity || !boundary.Equals("\r")) return 22;
                    text.ReplaceLineEndings("\n");
                    if (!text.Equals("a\nb\nc\n")) return 23;
                }
                if (allocations != releases) return 24;
                return 0;
            }
            """, optimization);

    private static void Run(string source, int optimization)
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("string-line-endings.gf", source));
        string ir = Compiler.Emit(files, options: new() { InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
