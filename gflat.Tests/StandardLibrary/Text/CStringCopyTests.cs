using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class CStringCopyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryOverlapDirectionMatchesDotNetSpanCopyAndShortCapacitiesAreAtomic(int optimization)
    {
        int[] offsets = [0, 1, 3, 7, 15, 23, 31];
        int[] sizes = [0, 1, 2, 3, 7, 15, 23];
        var cases = (from source in offsets from target in offsets from length in sizes select (source, target, length)).ToArray();
        var expected = new List<byte>();
        foreach (var item in cases)
        {
            byte[] bytes = Enumerable.Range(0, 64).Select(index => (byte)(index * 37 % 255 + 1)).ToArray();
            bytes[item.source + item.length] = 0;
            bytes.AsSpan(item.source, item.length + 1).CopyTo(bytes.AsSpan(item.target));
            expected.AddRange(bytes);
        }
        string program = $$"""
            using std; using std::Text;
            int main() {
                nuint[{{cases.Length}}] sources = [{{string.Join(",", cases.Select(item => "(nuint)" + item.source))}}];
                nuint[{{cases.Length}}] targets = [{{string.Join(",", cases.Select(item => "(nuint)" + item.target))}}];
                nuint[{{cases.Length}}] lengths = [{{string.Join(",", cases.Select(item => "(nuint)" + item.length))}}];
                readonly(char)* expected = "{{Convert.ToHexString(expected.ToArray())}}";
                readonly(char)* digits = "0123456789ABCDEF";
                char[66] storage; char[66] before;
                for (nuint test = 0; test < {{cases.Length}}; test++) {
                    storage[0] = '!'; storage[65] = '?';
                    for (nuint i = 0; i < 64; i++) storage[i + 1] = (char)(i * 37 % 255 + 1);
                    storage[sources[test] + lengths[test] + 1] = '\0';
                    for (nuint i = 0; i < 66; i++) before[i] = storage[i];
                    for (nuint capacity = 0; capacity <= lengths[test]; capacity++) {
                        if (CString::TryCopy(&storage[sources[test] + 1], &storage[targets[test] + 1], capacity)) return 1;
                        for (nuint i = 0; i < 66; i++) if (storage[i] != before[i]) return 2;
                    }
                    if (!CString::TryCopy(&storage[sources[test] + 1], &storage[targets[test] + 1], lengths[test] + 1)) return 3;
                    for (nuint i = 0; i < 64; i++) {
                        byte actual = (byte)storage[i + 1];
                        nuint position = (test * 64 + i) * 2;
                        if (digits[(nuint)(actual >> 4)] != expected[position] || digits[(nuint)(actual & (byte)15)] != expected[position + 1]) return 4;
                    }
                    if (storage[0] != '!' || storage[65] != '?') return 5;
                }
                return 0;
            }
            """;
        Run(program, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyZeroCapacityEmbeddedNullsAndDisjointHighBytesHaveExactBounds(int optimization)
    {
        Run("""
            using std; using std::Text;
            int main() {
                char*? absent = null;
                if (CString::TryCopy(absent!, absent!, 0)) return 1;
                char[6] output;
                for (nuint i = 0; i < 6; i++) output[i] = '!';
                if (CString::TryCopy("", &output[1], 0)) return 2;
                for (nuint i = 0; i < 6; i++) if (output[i] != '!') return 3;
                if (!CString::TryCopy("", &output[1], 1) || output[1] != '\0' || output[0] != '!' || output[2] != '!') return 4;
                if (!CString::TryCopy("A\0ignored", &output[1], 2) || output[1] != 'A' || output[2] != '\0' || output[3] != '!') return 5;
                char[4] input = [(char)128, (char)255, '\0', 'X'];
                if (!CString::TryCopy(input, &output[1], 4) || (byte)output[1] != (byte)128 || (byte)output[2] != (byte)255 || output[3] != '\0') return 6;
                if (output[0] != '!' || output[4] != '!' || output[5] != '!') return 7;
                char[2] unterminated = ['x', 'y'];
                char[6] before;
                for (nuint i = 0; i < 6; i++) before[i] = output[i];
                if (CString::TryCopy(unterminated, &output[1], 2)) return 8;
                for (nuint i = 0; i < 6; i++) if (output[i] != before[i]) return 9;
                return 0;
            }
            """, optimization);
    }

    private static void Run(string program, int optimization)
    {
        SourceFile[] sources = [
            new("CString.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Text/CString.gf"))),
            new("main.gf", program)
        ];
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true });
        foreach (string forbidden in new[] { "@__gflat_eh_", "personality", "gflat$Memory$", "@malloc", "@free" }) Assert.DoesNotContain(forbidden, ir);
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
