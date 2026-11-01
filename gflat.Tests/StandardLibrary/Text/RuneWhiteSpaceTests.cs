using System.Globalization;

namespace gflat.Tests.Std.Text;

public sealed class RuneWhiteSpaceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryValidScalarMatchesCompressedDotNetRangesWithoutAllocating(int optimization)
    {
        var ranges = new List<(uint Start, uint End)>();
        uint? start = null;
        for (int scalar = 0; scalar <= 0x110000; scalar++)
        {
            bool white = System.Text.Rune.IsValid(scalar) && System.Text.Rune.IsWhiteSpace(new System.Text.Rune(scalar));
            if (white && start == null) start = (uint)scalar;
            else if (!white && start != null)
            {
                ranges.Add((start.Value, (uint)scalar - 1));
                start = null;
            }
        }
        int expectedCount = ranges.Sum(range => (int)(range.End - range.Start + 1));
        Assert.Equal(25, expectedCount);
        string starts = string.Join(", ", ranges.Select(range => "0x" + range.Start.ToString("x", CultureInfo.InvariantCulture) + "u"));
        string ends = string.Join(", ", ranges.Select(range => "0x" + range.End.ToString("x", CultureInfo.InvariantCulture) + "u"));
        string program = $$"""
            using Standard::Text;

            int allocationAttempts = 0;
            namespace Memory {
                public replace void*? Allocate(nuint count) { allocationAttempts++; return null; }
                public replace void Free(void* storage) nothrow {}
            }
            bool Classify(Rune value) nothrow => Rune::IsWhiteSpace(value);
            int main() {
                uint[{{ranges.Count}}] starts = [{{starts}}];
                uint[{{ranges.Count}}] ends = [{{ends}}];
                nuint range = 0;
                uint matched = 0u;
                for (uint scalar = 0u; scalar <= 0x10ffffu; scalar++) {
                    if (scalar >= 0xd800u && scalar <= 0xdfffu) continue;
                    while (range < {{ranges.Count}} && scalar > ends[range]) range++;
                    bool expected = range < {{ranges.Count}} && scalar >= starts[range];
                    Rune value = new(scalar);
                    bool actual = Classify(value);
                    if (actual != expected || value.Value != scalar) return 1;
                    if (actual) matched++;
                }
                if (matched != {{expectedCount}}u) return 2;
                // Format characters and the historical Mongolian separator are not White_Space.
                uint[8] excluded = [0u, 0x84u, 0x86u, 0x180eu, 0x200bu, 0x200cu, 0x2060u, 0xfeffu];
                for (nuint index = 0; index < 8; index++)
                    if (Rune::IsWhiteSpace(new Rune(excluded[index]))) return 3;
                if (!Rune::IsWhiteSpace(new Rune(0x85u)) || !Rune::IsWhiteSpace(new Rune(0xa0u))
                    || !Rune::IsWhiteSpace(new Rune(0x2028u)) || !Rune::IsWhiteSpace(new Rune(0x2029u))) return 4;
                if (allocationAttempts != 0) return 5;
                return 0;
            }
            """;
        string[] names = ["core/Text/Rune.gf", "core/Text/utf8.gf", "core/Text/utf16.gf", "core/Text/String.gf",
            "core/Text/ByteSearch.gf", "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf",
            "core/IComparable.gf", "core/Hashing/primitive_hashing.gf", "core/primitive_format.gf",
            "core/Memory/Span.gf", "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf"];
        var sources = names.Select(name => new SourceFile(name,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("rune-whitespace.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
