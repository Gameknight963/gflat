namespace gflat.Tests.Std.Collections;

public sealed partial class SortedSetAlgebraTests
{
    private static void Run(string program, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/SortedMap.gf", "core/Collections/SortedSet.gf", "core/Collections/sorted_set_algebra.gf"))
            .Append(new SourceFile("sorted-set-algebra.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Sorted set algebra exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("SORTED_SET_ALGEBRA.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SmallSetMutationsMatchDotNetAcrossDifferentComparerEquivalenceClasses(int optimization)
    {
        IComparer<int>[] comparers = [Comparer<int>.Default, Comparer<int>.Create((a, b) => b.CompareTo(a)),
            Comparer<int>.Create((a, b) => (a / 2).CompareTo(b / 2)), Comparer<int>.Create((a, b) => (a % 2).CompareTo(b % 2))];
        string path = Path.Combine(Path.GetTempPath(), "gflat_sorted_algebra_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            for (byte leftComparer = 0; leftComparer < comparers.Length; leftComparer++)
            for (byte rightComparer = 0; rightComparer < comparers.Length; rightComparer++)
            for (byte leftMask = 0; leftMask < 32; leftMask++)
            for (byte rightMask = 0; rightMask < 32; rightMask++)
            for (byte operation = 0; operation < 5; operation++)
            {
                var target = new SortedSet<int>(comparers[leftComparer]);
                var other = new SortedSet<int>(comparers[rightComparer]);
                for (int value = 0; value < 5; value++)
                {
                    if ((leftMask & (1 << value)) != 0) target.Add(value);
                    if ((rightMask & (1 << value)) != 0) other.Add(value);
                }
                int removed = 0;
                switch (operation)
                {
                    case 0: target.UnionWith(other); break;
                    case 1: target.IntersectWith(other); break;
                    case 2: target.ExceptWith(other); break;
                    case 3: target.SymmetricExceptWith(other); break;
                    case 4: removed = target.RemoveWhere(value => value % 2 != 0); break;
                }
                int expected = 0;
                foreach (int value in target)
                {
                    int canonical = leftComparer == 2 ? value / 2 : leftComparer == 3 ? value % 2 : value;
                    expected |= 1 << canonical;
                }
                writer.Write(leftComparer); writer.Write(rightComparer); writer.Write(leftMask); writer.Write(rightMask);
                writer.Write(operation); writer.Write((byte)expected); writer.Write((byte)target.Count); writer.Write((byte)removed);
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
                bool Odd(immutable(int)* value) => *value % 2 != 0;
                int(immutable(int)*, immutable(int)*)* Choose(byte kind)
                {
                    if (kind == (byte)0) return Ascending;
                    if (kind == (byte)1) return Descending;
                    if (kind == (byte)2) return Grouped;
                    return Parity;
                }
                int Mask(immutable(SortedSet<int>)* source, byte kind)
                {
                    int result = 0;
                    var iterator = source.GetEnumerator();
                    while (iterator.MoveNext())
                    {
                        int value = *iterator.Current;
                        if (kind == (byte)2) value = value / 2;
                        if (kind == (byte)3) value = value % 2;
                        result = result | (1 << value);
                    }
                    return result;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    byte[8] row = default(byte[8]);
                    while (true)
                    {
                        nuint read = fread(&row[0], 1, 8, file!);
                        if (read == 0) break;
                        if (read != 8) return 2;
                        SortedSet<int> target = new(Choose(row[0]));
                        SortedSet<int> other = new(Choose(row[1]));
                        for (int value = 0; value < 5; value++)
                        {
                            if (((int)row[2] & (1 << value)) != 0) target.Add(value);
                            if (((int)row[3] & (1 << value)) != 0) other.Add(value);
                        }
                        int before = Mask(&other, row[1]);
                        nuint removed = 0;
                        if (row[4] == (byte)0) target.UnionWith(&other);
                        if (row[4] == (byte)1) target.IntersectWith(&other);
                        if (row[4] == (byte)2) target.ExceptWith(&other);
                        if (row[4] == (byte)3) target.SymmetricExceptWith(&other);
                        if (row[4] == (byte)4) removed = target.RemoveWhere(Odd);
                        if (Mask(&target, row[0]) != (int)row[5] || target.Count != (nuint)row[6] || removed != (nuint)row[7])
                        {
                            printf("comparers=%d,%d masks=%d,%d operation=%d expected=%d actual=%d\n", (int)row[0], (int)row[1], (int)row[2], (int)row[3], (int)row[4], (int)row[5], Mask(&target, row[0]));
                            return 3;
                        }
                        if (Mask(&other, row[1]) != before) return 4;
                    }
                    if (ferror(file!) != 0) return 5;
                    return 0;
                }
                """, optimization);
        }
        finally { File.Delete(path); }
    }
}
