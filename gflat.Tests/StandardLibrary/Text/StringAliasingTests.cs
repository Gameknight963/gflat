namespace gflat.Tests.Std.Text;

public sealed class StringAliasingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SelfInsertionSnapshotsReadableStorageBeyondTrimmedLengthIncludingTerminators(int optimization)
        => Run("""
            using std; using std::Text;
            int main() {
                for (int padded = 0; padded < 2; padded++) {
                    nuint maximum = 7;
                    if (padded != 0) maximum = 9;
                    for (nuint at = 0; at <= 2; at++) {
                        for (nuint start = 0; start <= maximum; start++) {
                            for (nuint count = 0; count <= maximum + 1 - start; count++) {
                                String value = String::From("ab     ");
                                if (padded != 0) value.PadRight(9, '\t');
                                value.TrimEnd();
                                // Every capacity byte was initialized before trimming, including
                                // bytes beyond the newly inserted terminator at Length.
                                char[10] before;
                                for (nuint i = 0; i <= value.Capacity; i++) before[i] = value.Data[i];
                                value.Insert(at, value.Data + start, count);
                                if (value.Length != 2 + count || value.Data[value.Length] != '\0') return 1;
                                for (nuint i = 0; i < at; i++) if (value.Data[i] != before[i]) return 2;
                                for (nuint i = 0; i < count; i++) if (value.Data[at + i] != before[start + i]) return 3;
                                for (nuint i = at; i < 2; i++) if (value.Data[count + i] != before[i]) return 4;
                            }
                        }
                    }
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedSelfAppendAndFromCopyEverySourceByteIncludingTheOldTerminator(int optimization)
        => Run("""
            using std; using std::Text;
            int main() {
                for (int reserved = 0; reserved < 2; reserved++) {
                    for (nuint start = 0; start <= 5; start++) {
                        for (nuint count = 0; count <= 6 - start; count++) {
                            String value = String::From("ab\0cd", 5);
                            if (reserved != 0) value.Reserve(32);
                            String copy = String::From(value.Data + start, count);
                            value.Append(value.Data + start, count);
                            if (value.Length != 5 + count || value.Data[value.Length] != '\0' ||
                                copy.Length != count || copy.Data[count] != '\0') return 1;
                            for (nuint i = 0; i < count; i++)
                                if (value.Data[5 + i] != copy.Data[i]) return 2;
                            value.Clear();
                            for (nuint i = 0; i < count; i++) if (copy.Data[i] != "ab\0cd"[start + i]) return 3;
                        }
                    }
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SnapshotFailureForTrimmedTailLeavesOriginalStorageAndInitializedBytesUntouched(int optimization)
        => Run("""
            using std; using std::Text;
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            namespace Memory {
                public replace void*? Allocate(nuint size) { if (size == 3) return null; return malloc(size); }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            int main() {
                String value = String::From("ab   ");
                value.TrimEnd();
                readonly(char)* before = value.Data;
                nuint capacity = value.Capacity;
                bool caught = false;
                try { value.Insert(0, value.Data + 3, 2); }
                catch (ref BadAllocException error) { caught = true; }
                if (!caught || value.Data != before || value.Capacity != capacity || value.Length != 2) return 1;
                if (value.Data[0] != 'a' || value.Data[1] != 'b' || value.Data[2] != '\0' ||
                    value.Data[3] != ' ' || value.Data[4] != ' ' || value.Data[5] != '\0') return 2;
                // Disjoint input with enough existing capacity needs no snapshot.
                value.Insert(0, "xy", 2);
                if (value.Data != before || value.Length != 4 || !value.StartsWith("xyab")) return 3;
                return 0;
            }
            """, optimization);

    private static IEnumerable<SourceFile> Sources(string code)
        => new[] { "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf", "IStringConvertable.gf", "Text/String.gf", "Text/ByteSearch.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", code));

    private static void Run(string code, int optimization)
        => Assert.Equal(0, CompilerTestHelper.Run(Sources(code), optimization).ExitCode);
}
