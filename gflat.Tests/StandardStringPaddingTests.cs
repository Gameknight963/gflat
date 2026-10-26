using System.Text;

namespace gflat.Tests;

public sealed class StandardStringPaddingTests
{
    private const string Counters = """
        using std;
        extern void*? malloc(nuint size);
        extern void free(void* storage);
        int allocations = 0;
        int releases = 0;
        namespace Memory {
            public replace void*? Allocate(nuint size) { allocations++; return malloc(size); }
            public replace void Free(void* storage) nothrow { releases++; free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BytePaddingMatchesDotNetAndUsesExistingCapacityWhenPossible(int optimization)
    {
        var random = new Random(71385);
        var code = new StringBuilder(Counters + "int main() { char[64] input; char[64] expected;\n");
        for (int sample = 0; sample < 40; sample++)
        {
            byte[] input = new byte[sample < 3 ? sample : random.Next(40)];
            random.NextBytes(input);
            byte padding = sample switch { 0 => 0, 1 => 255, 2 => 128, _ => (byte)random.Next(256) };
            int width = sample < 3 ? 7 : random.Next(61);
            Assign(code, "input", input);
            for (int side = 0; side < 2; side++)
            {
                string text = Encoding.Latin1.GetString(input);
                string padded = side == 0 ? text.PadLeft(width, (char)padding) : text.PadRight(width, (char)padding);
                Assign(code, "expected", Encoding.Latin1.GetBytes(padded));
                foreach (bool reserved in new[] { false, true })
                {
                    code.AppendLine($$"""
                        {
                            String value = String::From(input, {{input.Length}});
                            {{(reserved ? "value.Reserve(64);" : "")}}
                            readonly(char)* before = value.Data;
                            nuint capacity = value.Capacity;
                            int expectedFree = 0;
                            if (capacity != 0) expectedFree = 1;
                            int allocated = allocations;
                            int freed = releases;
                            value.{{(side == 0 ? "PadLeft" : "PadRight")}}({{width}}, (char){{padding}});
                            if (value.Length != {{padded.Length}} || value.Data[value.Length] != '\0') return 1;
                            for (nuint i = 0; i < value.Length; i++) if (value.Data[i] != expected[i]) return 2;
                            if ({{width}} <= capacity) {
                                if (value.Data != before || value.Capacity != capacity || allocations != allocated || releases != freed) return 3;
                            } else if (allocations != allocated + 1 || releases != freed + expectedFree) return 4;
                            allocated = allocations; freed = releases; before = value.Data;
                            value.PadLeft(0); value.PadRight(value.Length);
                            if (allocations != allocated || releases != freed || value.Data != before) return 5;
                        }
                        """);
                }
            }
        }
        code.AppendLine("if (allocations != releases) return 6; return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DefaultsEmptySentinelAndEveryPaddingBytePreserveCountedContents(int optimization)
        => Run(Counters + """
            int main() {
                {
                    String empty = new();
                    readonly(char)* sentinel = empty.Data;
                    empty.PadLeft(0); empty.PadRight(0);
                    if (allocations != 0 || empty.Data != sentinel || empty.Capacity != 0) return 1;
                    empty.PadLeft(2); empty.PadRight(4);
                    if (empty.Length != 4 || empty.Data[4] != '\0') return 2;
                    for (nuint i = 0; i < 4; i++) if (empty.Data[i] != ' ') return 3;
                    char[3] input = ['a', '\0', (char)255];
                    for (int padding = 0; padding < 256; padding++) {
                        String text = String::From(input, 3);
                        text.PadLeft(5, (char)padding); text.PadRight(7, (char)padding);
                        if (text.Length != 7 || text.Data[7] != '\0') return 4;
                        if (text.Data[0] != (char)padding || text.Data[1] != (char)padding ||
                            text.Data[5] != (char)padding || text.Data[6] != (char)padding) return 5;
                        if (text.Data[2] != 'a' || text.Data[3] != '\0' || text.Data[4] != (char)255) return 6;
                    }
                }
                if (allocations != releases) return 7;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedGrowthAndImpossibleWidthLeaveStorageAndContentsUnchanged(int optimization)
        => Run("""
            using std;
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            namespace Memory {
                public replace void*? Allocate(nuint size) { if (size != 4) return null; return malloc(size); }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            int main() {
                String value = String::From("a\0b", 3);
                readonly(char)* before = value.Data;
                nuint capacity = value.Capacity;
                int caught = 0;
                try { value.PadLeft(20, 'x'); } catch (ref BadAllocException error) { caught++; }
                try { value.PadRight(20, 'x'); } catch (ref BadAllocException error) { caught++; }
                if (caught != 2) return 1;
                nuint impossible = (nuint)0 - (nuint)1;
                try { value.PadLeft(impossible); } catch { caught++; }
                try { value.PadRight(impossible); } catch { caught++; }
                if (caught != 4 || value.Data != before || value.Capacity != capacity || value.Length != 3) return 2;
                if (value.Data[0] != 'a' || value.Data[1] != '\0' || value.Data[2] != 'b' || value.Data[3] != '\0') return 3;
                String empty = new(); before = empty.Data;
                try { empty.PadLeft(20); } catch (ref BadAllocException error) { caught++; }
                try { empty.PadRight(20); } catch (ref BadAllocException error) { caught++; }
                if (caught != 6 || empty.Data != before || empty.Capacity != 0 || empty.Length != 0 || empty.Data[0] != '\0') return 4;
                return 0;
            }
            """, optimization);

    private static void Assign(StringBuilder code, string name, byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++) code.AppendLine($"{name}[{i}] = (char){bytes[i]};");
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf", "IStringConvertable.gf", "String.gf", "Text/ByteSearch.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", code));

    private static void Run(string code, int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run(Sources(code), optimization).ExitCode);
}
