namespace gflat.Tests.Std.Collections;

public sealed class BitSetComplementTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("BIT_SET_COMPLEMENT.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ComplementPreservesLogicalLengthPaddingAndAllocationCount(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/BitSet.gf"))
            .Append(new SourceFile("bitset-complement.gf", """
            using Standard;
            using Standard::Collections;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            bool deny = false;
            int attempts = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny) return null;
                    return malloc(bytes);
                }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            void Invert(BitSet* bits) nothrow { bits.Not(); }
            int main()
            {
                for (nuint length = 0; length <= 130; length++)
                {
                    BitSet bits = new(length);
                    for (nuint i = 0; i < length; i++) bits[i] = i % 3 == 0;
                    BitSet original = bits.Clone();
                    int before = attempts;
                    deny = true;
                    Invert(&bits);
                    if (attempts != before || bits.Length != length) return 1;
                    nuint expectedCount = 0;
                    for (nuint i = 0; i < length; i++)
                    {
                        bool expected = i % 3 != 0;
                        if (bits[i] != expected) return 2;
                        if (expected) expectedCount++;
                    }
                    if (bits.CountSetBits() != expectedCount) return 3;
                    BitSet::Enumerator iterator = bits.GetEnumerator();
                    nuint enumerated = 0;
                    while (iterator.MoveNext())
                    {
                        if (iterator.Current >= length || iterator.Current % 3 == 0) return 4;
                        enumerated++;
                    }
                    if (enumerated != expectedCount) return 5;
                    bits.Not();
                    if (!bits.Equals(&original) || bits.GetHashCode() != original.GetHashCode()) return 6;
                    bits.Not();
                    deny = false;
                    bits.Resize(length + 35);
                    for (nuint i = length; i < bits.Length; i++) if (bits[i]) return 7;
                    if (bits.CountSetBits() != expectedCount) return 8;
                    bits.SetAll(true);
                    bits.Not();
                    if (bits.HasAnySet()) return 9;
                    bits.Not();
                    if (!bits.HasAllSet()) return 10;
                }
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"Complement exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
