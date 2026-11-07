namespace gflat.Tests.Std.Text;

public sealed class StringRangeViewTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Text/string_span.gf"))
            .Append(new SourceFile("string-range-views.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"String range exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("STRING_RANGE_VIEWS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallRangeAndSuffixPreservesBoundsAndElementProtection(int optimization)
        => Run("""
            using Standard;
            using Standard::Text;
            using Standard::Memory;

            int main()
            {
                for (nuint length = 0; length <= 24; length++)
                {
                    String owner = new();
                    owner.Resize(length);
                    for (nuint i = 0; i < length; i++) owner[i] = (char)i;
                    readonly(String)* readonlyOwner = &owner;
                    immutable(String)* immutableOwner = &owner;
                    for (nuint start = 0; start <= length + 1; start++)
                    {
                        for (nuint count = 0; count <= length + 1; count++)
                        {
                            bool valid = start <= length;
                            if (valid) valid = count <= length - start;
                            for (int form = 0; form < 3; form++)
                            {
                                bool caught = false;
                                Span<readonly(char)> result = new();
                                try
                                {
                                    if (form == 0) result = owner.AsSpan(start, count).AsReadOnly();
                                    if (form == 1) result = readonlyOwner.AsReadOnly(start, count);
                                    if (form == 2) result = AsImmutable(ref *immutableOwner, start, count);
                                }
                                catch (Exception* error) { caught = true; }
                                if (caught == valid) return 1;
                                if (valid)
                                {
                                    if (result.Length != count) return 2;
                                    for (nuint i = 0; i < count; i++) { if (result[i] != (char)(start + i)) return 3; }
                                    if (count == 0 && result.Data != null) return 4;
                                }
                                else if (result.Length != 0) return 5;
                            }
                        }
                        for (int form = 0; form < 3; form++)
                        {
                            bool caught = false;
                            Span<readonly(char)> result = new();
                            try
                            {
                                if (form == 0) result = AsSpan(ref owner, start).AsReadOnly();
                                if (form == 1) result = AsReadOnly(ref *readonlyOwner, start);
                                if (form == 2) result = immutableOwner.AsImmutable(start);
                            }
                            catch (Exception* error) { caught = true; }
                            if (caught != (start > length)) return 6;
                            if (!caught)
                            {
                                if (result.Length != length - start) return 7;
                                for (nuint i = 0; i < result.Length; i++) { if (result[i] != (char)(start + i)) return 8; }
                            }
                        }
                    }
                    if (owner.Length != length || owner.Data[length] != '\0') return 9;
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SuccessfulViewsAllocateNothingAndBorrowOnlyCountedBytes(int optimization)
        => Run("""
            using Standard;
            using Standard::Text;
            using Standard::Memory;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { attempts++; return malloc(bytes); }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            int main()
            {
                String owner = s"ab\0cd";
                owner.Reserve(100);
                int before = attempts;
                Span<char> mutable = owner.AsSpan(1, 3);
                Span<readonly(char)> first = owner.AsReadOnly(1, 3);
                Span<readonly(char)> second = owner.AsImmutable(1, 3);
                mutable[1] = 'X';
                if (owner[2] != 'X' || first[1] != 'X' || second[1] != 'X') return 1;
                if (mutable.Length != 3 || first.Length != 3 || second.Length != 3) return 2;
                if (owner.AsSpan(owner.Length).Length != 0 || owner.AsReadOnly(owner.Length).Length != 0 || owner.AsImmutable(owner.Length).Length != 0) return 3;
                if (attempts != before || owner.Length != 5 || owner.Data[5] != '\0') return 4;
                return 0;
            }
            """, optimization);
}
