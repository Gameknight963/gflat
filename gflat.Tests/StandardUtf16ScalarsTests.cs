using System.Text;

namespace gflat.Tests;

public sealed class StandardUtf16ScalarsTests
{
    private const string Allocator = """
        extern void*? malloc(nuint count);
        extern void free(void* storage);
        int allocations = 0; int releases = 0;
        namespace Memory {
            public replace void*? Allocate(nuint count) {
                void*? result = malloc(count); if (result != null) allocations++; return result;
            }
            public replace void Free(void* storage) nothrow { releases++; free(storage); }
        }
        """;

    private static void Run(string program, int optimization)
    {
        var sources = new[] { "Text/utf16.gf", "Text/Utf16Scalars.gf", "Collections/IEnumerable.gf", "Collections/IEnumerator.gf", "Queries/Enumerable.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", Allocator + program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ForeachConcreteAndInterfaceEnumerationMatchDotNetRunesWithoutAllocation(int optimization)
    {
        const string text = "A\0\u007f\u0080\u07ff\u0800\ud7ff\ue000\uffff\ufeff\U00010000\U0001f600\U0010ffffZ";
        uint[] scalars = text.EnumerateRunes().Select(r => (uint)r.Value).ToArray();
        Run($$"""
            int main() {
                ushort[{{text.Length}}] units = [{{string.Join(",", text.Select(c => $"(ushort){(int)c}"))}}];
                uint[{{scalars.Length}}] expected = [{{string.Join(",", scalars.Select(s => $"{s}u"))}}];
                std::Text::Utf16Scalars scalars = new(units, {{text.Length}});
                if (scalars.CodeUnitLength != {{text.Length}}) return 1;
                for (int pass = 0; pass < 2; pass++) {
                    nuint index = 0;
                    foreach (uint value in scalars) {
                        if (index >= (nuint){{scalars.Length}} || value != expected[index]) return 2;
                        index++;
                    }
                    if (index != (nuint){{scalars.Length}}) return 3;
                }
                std::Text::Utf16Scalars::Enumerator first = scalars.GetEnumerator();
                std::Text::Utf16Scalars::Enumerator second = scalars.GetEnumerator();
                if (!first.MoveNext() || first.Current != 65u || !first.MoveNext() || first.Current != 0u) return 4;
                if (!second.MoveNext() || second.Current != 65u) return 5;
                std::Text::Utf16Scalars::Enumerator copy = second;
                if (!copy.MoveNext() || copy.Current != 0u || second.Current != 65u) return 6;
                std::Collections::IEnumerable<uint, std::Text::Utf16Scalars::Enumerator>* source = &scalars;
                std::Text::Utf16Scalars::Enumerator iterator = source.GetEnumerator();
                nuint index = 0;
                while (iterator.MoveNext()) {
                    if (index >= (nuint){{scalars.Length}} || iterator.Current != expected[index]) return 7;
                    index++;
                }
                if (index != (nuint){{scalars.Length}} || allocations != 0 || releases != 0) return 8;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedStorageCanChangeWithoutChangingCachedCurrent(int optimization)
    {
        Run("""
            int main() {
                ushort[3] units = [(ushort)65,(ushort)66,(ushort)67];
                std::Text::Utf16Scalars scalars = new(units, 3); std::Text::Utf16Scalars copy = scalars;
                std::Text::Utf16Scalars::Enumerator iterator = scalars.GetEnumerator();
                if (!iterator.MoveNext() || iterator.Current != 65u) return 1;
                units[0] = (ushort)88; units[1] = (ushort)89;
                if (iterator.Current != 65u || !iterator.MoveNext() || iterator.Current != 89u) return 2;
                std::Text::Utf16Scalars::Enumerator fresh = copy.GetEnumerator();
                if (!fresh.MoveNext() || fresh.Current != 88u) return 3;
                foreach (uint value in scalars) { if (value != 88u) return 4; break; }
                if (units[0] != (ushort)88 || allocations != 0 || releases != 0) return 5;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyNullAndCurrentStatesMatchOtherEnumerators(int optimization)
    {
        Run("""
            int main() {
                std::Text::Utf16Scalars empty = new(); std::Text::Utf16Scalars nullEmpty = new(null, 0);
                if (empty.CodeUnitLength != 0 || nullEmpty.CodeUnitLength != 0) return 1;
                foreach (uint value in empty) return 2;
                std::Text::Utf16Scalars::Enumerator iterator = empty.GetEnumerator();
                bool caught = false; try { iterator.Current; } catch { caught = true; }
                if (!caught || iterator.MoveNext() || iterator.MoveNext()) return 3;
                caught = false; try { iterator.Current; } catch { caught = true; }
                if (!caught) return 4;
                caught = false; try { new std::Text::Utf16Scalars(null, 1); } catch { caught = true; }
                if (!caught) return 5;
                caught = false; try { new std::Text::Utf16Scalars::Enumerator(null, 1); } catch { caught = true; }
                if (!caught) return 6;
                ushort unit = (ushort)65; iterator = new std::Text::Utf16Scalars(&unit, 1).GetEnumerator();
                caught = false; try { iterator.Current; } catch { caught = true; }
                if (!caught || !iterator.MoveNext() || iterator.Current != 65u || iterator.MoveNext()) return 7;
                caught = false; try { iterator.Current; } catch { caught = true; }
                if (!caught || allocations == 0 || allocations != releases) return 8;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MalformedInputFailsOnlyWhenReachedInvalidatesCurrentAndCanBeRetried(int optimization)
    {
        ushort[][] invalid = [[0xd800], [0xdbff], [0xdc00], [0xdfff], [0xd800, 65], [0xd800, 0xd800], [0xdbff, 0xffff], [0xdc00, 0xd800]];
        string cases = string.Join("\n", invalid.Select(units => $$"""
            {
                ushort[{{units.Length + 1}}] units = [{{string.Join(",", new ushort[] { 65 }.Concat(units).Select(u => $"(ushort){u}"))}}];
                std::Text::Utf16Scalars scalars = new(units, {{units.Length + 1}});
                std::Text::Utf16Scalars invalidStart = new(&units[1], {{units.Length}});
                std::Text::Utf16Scalars::Enumerator invalidIterator = invalidStart.GetEnumerator();
                bool initialCaught = false; try { invalidIterator.MoveNext(); } catch { initialCaught = true; }
                if (!initialCaught) return 8;
                std::Text::Utf16Scalars::Enumerator iterator = scalars.GetEnumerator();
                if (!iterator.MoveNext() || iterator.Current != 65u) return 1;
                bool caught = false; try { iterator.MoveNext(); } catch { caught = true; }
                if (!caught) return 2;
                caught = false; try { iterator.Current; } catch { caught = true; }
                if (!caught) return 3;
                caught = false; try { iterator.MoveNext(); } catch { caught = true; }
                if (!caught) return 4;
                foreach (uint value in scalars) { if (value != 65u) return 5; break; }
                // Failure does not consume input: fixing borrowed storage permits retry.
                units[1] = (ushort)66;
                if (!iterator.MoveNext() || iterator.Current != 66u) return 6;
            }
            """));
        Run($$"""
            int main() {
                {{cases}}
                if (allocations == 0 || allocations != releases) return 7;
                return 0;
            }
            """, optimization);
    }
}
