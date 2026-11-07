using System.Text;

namespace gflat.Tests.Std.Queries;

public sealed partial class IndexQueryTests
{
    private static void Run(string program, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Queries/Indexed.gf", "core/Queries/IndexSource.gf", "core/Queries/enumerable_index.gf",
            "core/Queries/SliceSource.gf", "core/Queries/enumerable_slice.gf", "core/Queries/enumerable_to_vector.gf",
            "core/Queries/enumerable_count.gf"))
            .Append(new SourceFile("index-query-tests.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Index query exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("QUERY_INDEX.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PositionsAndItemsMatchLinqAcrossRepeatedEnumerationAndSlicing(int optimization)
    {
        List<int[]> cases = [[], [1], [3, 1, 3, 2], [int.MinValue, 0, int.MaxValue]];
        var random = new Random(192847);
        for (int i = 0; i < 64; i++) cases.Add(Enumerable.Range(0, random.Next(65)).Select(_ => random.Next(-1000, 1001)).ToArray());
        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, Encoding.UTF8, true))
            foreach (var input in cases)
            {
                writer.Write((byte)input.Length);
                foreach (int value in input) writer.Write(value);
                foreach (var value in input.Index()) { writer.Write((ulong)value.Index); writer.Write(value.Item); }
            }
        string hex = Convert.ToHexString(packet.ToArray()).ToLowerInvariant();
        Run($$"""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Queries;

            int Byte(readonly(char)* data, ref nuint position)
            {
                int result = 0;
                for (int i = 0; i < 2; i++)
                {
                    char digit = data[position]; position++;
                    int value = (int)digit - (int)'0';
                    if (digit >= 'a') value = (int)digit - (int)'a' + 10;
                    result = result * 16 + value;
                }
                return result;
            }
            ulong Read(readonly(char)* data, ref nuint position, int bytes)
            {
                ulong result = 0ul;
                for (int i = 0; i < bytes; i++) result = result | ((ulong)Byte(data, ref position) << (i * 8));
                return result;
            }
            int main()
            {
                readonly(char)* data = "{{hex}}";
                nuint position = 0;
                int[64] values = default(int[64]);
                for (int sample = 0; sample < {{cases.Count}}; sample++)
                {
                    nuint count = (nuint)Byte(data, ref position);
                    for (nuint i = 0; i < count; i++) values[i] = (int)Read(data, ref position, 4);
                    Span<int> span = new(values, count);
                    var query = span.AsEnumerable().Index();
                    Vector<Indexed<int>> first = query.ToVector();
                    Vector<Indexed<int>> again = query.ToVector();
                    Vector<Indexed<int>> free = Index(span.AsEnumerable()).ToVector();
                    if (first.Count != count || again.Count != count || free.Count != count || query.Count() != count) return 1;
                    for (nuint i = 0; i < count; i++)
                    {
                        nuint index = (nuint)Read(data, ref position, 8);
                        int item = (int)Read(data, ref position, 4);
                        if (first[i].Index != index || first[i].Item != item || again[i].Index != index || again[i].Item != item
                            || free[i].Index != index || free[i].Item != item) return 2;
                    }
                    Vector<Indexed<int>> afterIndex = query.Skip(2).Take(3).ToVector();
                    for (nuint i = 0; i < afterIndex.Count; i++)
                        if (afterIndex[i].Index != i + 2 || afterIndex[i].Item != values[i + 2]) return 3;
                    Vector<Indexed<int>> beforeIndex = span.AsEnumerable().Skip(2).Index().ToVector();
                    for (nuint i = 0; i < beforeIndex.Count; i++)
                        if (beforeIndex[i].Index != i || beforeIndex[i].Item != values[i + 2]) return 4;
                }
                if (position != (nuint){{hex.Length}}) return 5;
                return 0;
            }
            """, optimization);
    }
}
