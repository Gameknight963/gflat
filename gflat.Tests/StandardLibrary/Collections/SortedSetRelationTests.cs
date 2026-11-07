namespace gflat.Tests.Std.Collections;

public sealed partial class SortedSetRelationTests
{
    private static void Run(string program, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/SortedMap.gf", "core/Collections/SortedSet.gf"))
            .Append(new SourceFile("sorted-set-relations.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Sorted set relations exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("SORTED_SET_RELATIONS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExhaustiveSmallSetsMatchDotNetWithEqualAndDifferentComparers(int optimization)
    {
        IComparer<int>[] comparers = [Comparer<int>.Default, Comparer<int>.Create((a, b) => b.CompareTo(a)),
            Comparer<int>.Create((a, b) => (a / 2).CompareTo(b / 2)), Comparer<int>.Create((a, b) => (a % 2).CompareTo(b % 2))];
        string path = Path.Combine(Path.GetTempPath(), "gflat_sorted_relations_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            for (byte leftComparer = 0; leftComparer < comparers.Length; leftComparer++)
            for (byte rightComparer = 0; rightComparer < comparers.Length; rightComparer++)
            for (byte leftMask = 0; leftMask < 64; leftMask++)
            for (byte rightMask = 0; rightMask < 64; rightMask++)
            {
                var left = new SortedSet<int>(comparers[leftComparer]);
                var right = new SortedSet<int>(comparers[rightComparer]);
                for (int value = 0; value < 6; value++)
                {
                    if ((leftMask & (1 << value)) != 0) left.Add(value);
                    if ((rightMask & (1 << value)) != 0) right.Add(value);
                }
                int expected = (left.SetEquals(right) ? 1 : 0) | (left.IsSubsetOf(right) ? 2 : 0)
                    | (left.IsProperSubsetOf(right) ? 4 : 0) | (left.IsSupersetOf(right) ? 8 : 0)
                    | (left.IsProperSupersetOf(right) ? 16 : 0) | (left.Overlaps(right) ? 32 : 0);
                writer.Write(leftComparer); writer.Write(rightComparer); writer.Write(leftMask); writer.Write(rightMask);
                writer.Write((byte)expected); writer.Write((byte)left.Count); writer.Write((byte)right.Count);
            }
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            Run($$"""
                using Standard;
                using Standard::Collections;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int ferror(void* file);
                extern "C" int fclose(void* file);
                extern "C" int printf(readonly(char)* format, ...);
                int Ascending(immutable(int)* left, immutable(int)* right) => *left - *right;
                int Descending(immutable(int)* left, immutable(int)* right) => *right - *left;
                int Grouped(immutable(int)* left, immutable(int)* right) => *left / 2 - *right / 2;
                int Parity(immutable(int)* left, immutable(int)* right) => *left % 2 - *right % 2;
                int(immutable(int)*, immutable(int)*)* Choose(byte kind)
                {
                    if (kind == (byte)0) return Ascending;
                    if (kind == (byte)1) return Descending;
                    if (kind == (byte)2) return Grouped;
                    return Parity;
                }
                int Mask(immutable(SortedSet<int>)* source)
                {
                    int result = 0;
                    var iterator = source.GetEnumerator();
                    while (iterator.MoveNext()) result = result | (1 << *iterator.Current);
                    return result;
                }
                int Relations(immutable(SortedSet<int>)* left, immutable(SortedSet<int>)* right)
                {
                    int result = 0;
                    if (left.SetEquals(right)) result = result | 1;
                    if (left.IsSubsetOf(right)) result = result | 2;
                    if (left.IsProperSubsetOf(right)) result = result | 4;
                    if (left.IsSupersetOf(right)) result = result | 8;
                    if (left.IsProperSupersetOf(right)) result = result | 16;
                    if (left.Overlaps(right)) result = result | 32;
                    return result;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    byte[7] row = default(byte[7]);
                    while (true)
                    {
                        nuint read = fread(&row[0], 1, 7, file!);
                        if (read == 0) break;
                        if (read != 7) return 2;
                        SortedSet<int> left = new(Choose(row[0]));
                        SortedSet<int> right = new(Choose(row[1]));
                        for (int value = 0; value < 6; value++)
                        {
                            if (((int)row[2] & (1 << value)) != 0) left.Add(value);
                            if (((int)row[3] & (1 << value)) != 0) right.Add(value);
                        }
                        int beforeLeft = Mask(&left);
                        int beforeRight = Mask(&right);
                        int actual = Relations(&left, &right);
                        if (actual != (int)row[4])
                        {
                            printf("comparers=%d,%d masks=%d,%d expected=%d actual=%d\n", (int)row[0], (int)row[1], (int)row[2], (int)row[3], (int)row[4], actual);
                            return 3;
                        }
                        int self = 11;
                        if (!left.IsEmpty) self = 43;
                        if (Relations(&left, &left) != self) return 4;
                        if (left.Count != (nuint)row[5] || right.Count != (nuint)row[6]
                            || Mask(&left) != beforeLeft || Mask(&right) != beforeRight) return 5;
                    }
                    if (ferror(file!) != 0) return 6;
                    return 0;
                }
                """, optimization);
        }
        finally { File.Delete(path); }
    }
}
