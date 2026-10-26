using System.Text;

namespace gflat.Tests;

public class StandardUtf8ScalarsTests
{
    private const string AllocationCounters = """
        extern void*? malloc(nuint count);
        extern void free(void* storage);
        int allocations = 0; int releases = 0;
        namespace Memory {
            public replace void*? Allocate(nuint count) {
                void*? result = malloc(count);
                if (result != null) allocations++;
                return result;
            }
            public replace void Free(void* storage) nothrow { releases++; free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ForeachAndIndependentConcreteAndInterfaceIteratorsMatchDotNetRunesWithoutAllocation(int optimization)
    {
        const string text = "A\0\u007f\u0080\u07ff\u0800\ud7ff\ue000\uffff\U00010000\U0001f600\U0010ffffZ";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        uint[] runes = text.EnumerateRunes().Select(rune => (uint)rune.Value).ToArray();
        string data = string.Join(",", bytes.Select(b => $"(char){b}"));
        string expected = string.Join(",", runes.Select(rune => $"{rune}u"));
        Run($$"""
            int main() {
                char[{{bytes.Length}}] bytes = [{{data}}];
                uint[{{runes.Length}}] expected = [{{expected}}];
                std::Text::Utf8Scalars scalars = new(bytes, {{bytes.Length}});
                if (scalars.ByteLength != {{bytes.Length}}) return 1;
                for (int pass = 0; pass < 2; pass++) {
                    nuint index = 0;
                    foreach (uint value in scalars) {
                        if (index >= (nuint){{runes.Length}} || value != expected[index]) return 2;
                        index++;
                    }
                    if (index != (nuint){{runes.Length}}) return 3;
                }
                std::Text::Utf8Scalars::Enumerator first = scalars.GetEnumerator();
                std::Text::Utf8Scalars::Enumerator second = scalars.GetEnumerator();
                if (!first.MoveNext() || first.Current != 65u) return 4;
                if (!first.MoveNext() || first.Current != 0u) return 5;
                if (!second.MoveNext() || second.Current != 65u) return 6;
                std::Text::Utf8Scalars::Enumerator copy = second;
                if (!copy.MoveNext() || copy.Current != 0u || second.Current != 65u) return 7;
                std::Collections::IEnumerable<uint, std::Text::Utf8Scalars::Enumerator>* source = &scalars;
                std::Text::Utf8Scalars::Enumerator iterator = source.GetEnumerator();
                nuint count = 0;
                while (iterator.MoveNext()) {
                    if (iterator.Current != expected[count]) return 8;
                    count++;
                }
                if (count != (nuint){{runes.Length}} || allocations != 0 || releases != 0) return 9;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ViewsBorrowBytesWhileCurrentCachesTheLastDecodedScalar(int optimization)
        => Run("""
            int main() {
                char[3] bytes = ['A','B','C'];
                std::Text::Utf8Scalars scalars = new(bytes, 3);
                std::Text::Utf8Scalars copied = scalars;
                std::Text::Utf8Scalars::Enumerator iterator = scalars.GetEnumerator();
                if (!iterator.MoveNext() || iterator.Current != 65u) return 1;
                bytes[0] = 'X'; bytes[1] = 'Y';
                if (iterator.Current != 65u || !iterator.MoveNext() || iterator.Current != 89u) return 2;
                std::Text::Utf8Scalars::Enumerator fresh = copied.GetEnumerator();
                if (!fresh.MoveNext() || fresh.Current != 88u) return 3;
                // Breaking enumeration neither changes nor owns caller storage.
                foreach (uint scalar in scalars) { if (scalar != 88u) return 4; break; }
                if (bytes[0] != 'X' || allocations != 0 || releases != 0) return 5;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyAndInvalidIteratorStatesThrowAndExceptionsReleaseTheirStorage(int optimization)
        => Run("""
            int main() {
                std::Text::Utf8Scalars empty = new();
                std::Text::Utf8Scalars nullEmpty = new(null, 0);
                if (empty.ByteLength != 0 || nullEmpty.ByteLength != 0) return 1;
                foreach (uint scalar in empty) return 2;
                std::Text::Utf8Scalars::Enumerator iterator = empty.GetEnumerator();
                bool caught = false;
                try { iterator.Current; } catch { caught = true; }
                if (!caught || iterator.MoveNext() || iterator.MoveNext()) return 3;
                caught = false; try { iterator.Current; } catch { caught = true; }
                if (!caught) return 4;
                caught = false; try { new std::Text::Utf8Scalars(null, 1); } catch { caught = true; }
                if (!caught) return 5;
                caught = false; try { new std::Text::Utf8Scalars::Enumerator(null, 1); } catch { caught = true; }
                if (!caught) return 6;
                iterator = new std::Text::Utf8Scalars("A", 1).GetEnumerator();
                caught = false; try { iterator.Current; } catch { caught = true; }
                if (!caught || !iterator.MoveNext() || iterator.Current != 65u || iterator.MoveNext()) return 7;
                caught = false; try { iterator.Current; } catch { caught = true; }
                if (!caught || allocations == 0 || allocations != releases) return 8;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MalformedAndTruncatedBytesThrowOnlyWhenReachedAndInvalidateCurrent(int optimization)
    {
        string[] malformed = ["80", "BF", "C0AF", "C180", "C2", "C241", "E08080", "E19F", "EDA080", "F0808080", "F4908080", "F5808080", "F18080", "FF"];
        string checks = string.Join("\n", malformed.Select(hex =>
        {
            byte[] bytes = Convert.FromHexString("41" + hex);
            string data = string.Join(",", bytes.Select(b => $"(char){b}"));
            return $$"""
                {
                    char[{{bytes.Length}}] bytes = [{{data}}];
                    std::Text::Utf8Scalars scalars = new(bytes, {{bytes.Length}});
                    std::Text::Utf8Scalars::Enumerator iterator = scalars.GetEnumerator();
                    if (!iterator.MoveNext() || iterator.Current != 65u) return 1;
                    bool caught = false; try { iterator.MoveNext(); } catch { caught = true; }
                    if (!caught) return 2;
                    caught = false; try { iterator.Current; } catch { caught = true; }
                    if (!caught) return 3;
                    caught = false; try { iterator.MoveNext(); } catch { caught = true; }
                    if (!caught) return 4;
                    // Early termination never examines the malformed suffix.
                    foreach (uint scalar in scalars) { if (scalar != 65u) return 5; break; }
                }
                """;
        }));
        Run($$"""
            int main() {
                {{checks}}
                if (allocations == 0 || allocations != releases) return 6;
                return 0;
            }
            """, optimization);
    }

    private static void Run(string program, int optimization)
    {
        var sources = new[] { "Text/utf8.gf", "Text/Utf8Scalars.gf", "Collections/IEnumerable.gf", "Collections/IEnumerator.gf", "Queries/Enumerable.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", AllocationCounters + program));
        string ir = Compiler.Emit(sources);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-utf8-scalars-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "scalars.ll"), output = Path.Combine(directory, "scalars.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(output, []);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
