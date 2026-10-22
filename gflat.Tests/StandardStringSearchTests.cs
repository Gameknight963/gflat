using System.Text;

namespace gflat.Tests;

public sealed class StandardStringSearchTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedSearchesMatchByteOracleWithStartOffsetsAndRepeatedPatterns(int optimization)
    {
        var random = new Random(82914);
        byte[] alphabet = [0, (byte)'a', (byte)'b', 255];
        var code = new StringBuilder("using std; int main() { char[96] input; char[32] pattern;\n");
        for (int sample = 0; sample < 96; sample++)
        {
            byte[] input = new byte[random.Next(97)], pattern = new byte[random.Next(33)];
            for (int i = 0; i < input.Length; i++) input[i] = alphabet[random.Next(alphabet.Length)];
            for (int i = 0; i < pattern.Length; i++) pattern[i] = alphabet[random.Next(alphabet.Length)];
            if (sample % 2 == 0 && pattern.Length <= input.Length)
                input.AsSpan(random.Next(input.Length - pattern.Length + 1), pattern.Length).CopyTo(pattern);
            Assign(code, "input", input); Assign(code, "pattern", pattern);
            code.AppendLine($"{{ String text = String::From(input, {input.Length}); immutable(String)* view = &text;");
            foreach (int start in new[] { 0, input.Length / 2, input.Length }.Distinct())
            {
                int offset = input.AsSpan(start).IndexOf(pattern);
                int expected = offset < 0 ? -1 : offset + start;
                code.AppendLine($"if (view.IndexOf(pattern, {pattern.Length}, {start}) != {Index(expected)}) return 1;");
            }
            code.AppendLine($"if (view.LastIndexOf(pattern, {pattern.Length}) != {Index(input.AsSpan().LastIndexOf(pattern))}) return 2;");
            code.AppendLine($"if (view.Contains(pattern, {pattern.Length}) != {(input.AsSpan().IndexOf(pattern) >= 0 ? "true" : "false")}) return 3; }}");
        }
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LongNearMatchesAndAliasedPatternsKeepSentinelsAndAllocateNothingDuringSearch(int optimization)
        => Run("""
            using std;
            extern void*? malloc(nuint count);
            extern void free(void* storage);
            int allocations = 0;
            namespace Memory {
                replace void*? Allocate(nuint count) { allocations++; return malloc(count); }
                replace void Free(void* storage) { free(storage); }
            }
            int main() {
                char[65536] input; char[2048] pattern;
                for (nuint i = 0; i < 65536; i++) input[i] = 'a';
                for (nuint i = 0; i < 2048; i++) pattern[i] = 'a';
                pattern[2046] = 'b';
                String text = String::From(input, 65536);
                int allocated = allocations;
                if (text.IndexOf(pattern, 2048) != String::NotFound || text.LastIndexOf(pattern, 2048) != String::NotFound) return 1;
                text[65534] = 'b';
                if (text.IndexOf(pattern, 2048) != 63488 || text.LastIndexOf(pattern, 2048) != 63488) return 2;
                if (text.IndexOf(pattern, 2048, 63488) != 63488 || text.IndexOf(pattern, 2048, 63489) != String::NotFound) return 3;
                if (text.IndexOf(text.Data + 63488, 2048) != 63488 || text.LastIndexOf(text.Data + 63488, 2048) != 63488) return 4;
                if (text.IndexOf((readonly(char)*)1, 0, 65536) != 65536 || text.LastIndexOf((readonly(char)*)1, 0) != 65536) return 5;
                nuint maximum = (nuint)0 - (nuint)1;
                if (text.IndexOf((readonly(char)*)1, maximum) != String::NotFound || text.LastIndexOf((readonly(char)*)1, maximum) != String::NotFound) return 6;
                if (text.Data[65536] != '\0' || allocations != allocated) return 7;
                String empty = new();
                if (empty.IndexOf((readonly(char)*)1, 0) != 0 || empty.LastIndexOf((readonly(char)*)1, 0) != 0) return 8;
                if (empty.IndexOf((readonly(char)*)1, 1) != String::NotFound || empty.LastIndexOf((readonly(char)*)1, 1) != String::NotFound) return 9;
                bool caught = false;
                try { text.IndexOf(pattern, 0, 65537); } catch (ref Exception error) { caught = true; }
                if (!caught || text.Length != 65536) return 10;
                return 0;
            }
            """, optimization);

    private static string Index(int value) => value < 0 ? "String::NotFound" : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static void Assign(StringBuilder code, string name, byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++) code.AppendLine($"{name}[{i}] = (char){bytes[i]};");
    }
    private static void Run(string code, int optimization)
    {
        var sources = new[] { "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf", "IStringConvertable.gf", "String.gf", "ByteSearch.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", code));
        Assert.Equal(0, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }
}
