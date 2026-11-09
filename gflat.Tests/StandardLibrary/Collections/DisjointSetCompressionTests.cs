using gflat.CompileExceptions;

namespace gflat.Tests.Std.Collections;

public sealed class DisjointSetCompressionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MutableLookupsPreserveRepresentativesAndSizesWithoutAllocating(int optimization)
    {
        const string code = """
            using Standard;
            using Standard::Collections;

            extern "C" void*? malloc(nuint count);
            extern "C" void free(void* storage);
            int attempts = 0;
            int allocated = 0;
            int released = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint count)
                {
                    attempts++;
                    if (deny) return null;
                    void*? storage = malloc(count);
                    if (storage != null) allocated++;
                    return storage;
                }
                public replace void Free(void* storage) nothrow
                {
                    released++;
                    free(storage);
                }
            }
            int main()
            {
                {
                    DisjointSet groups = new(64);
                    for (nuint width = 1; width < 64; width *= 2)
                        for (nuint first = 0; first < 64; first += width * 2)
                            groups.Union(first, first + width);
                    nuint root = groups.Find(0);
                    for (int which = 0; which < 3; which++)
                    {
                        bool caught = false;
                        try
                        {
                            if (which == 0) groups.FindAndCompress(64);
                            if (which == 1) groups.Union(63, 64);
                            if (which == 2) groups.Union(64, 63);
                        }
                        catch (ref Exception error) { caught = true; }
                        if (!caught || groups.ElementCount != 64 || groups.SetCount != 1) return 1;
                        for (nuint index = 0; index < 64; index++)
                            if (groups.Find(index) != root || groups.SizeOf(index) != 64) return 2;
                    }
                    int before = attempts;
                    deny = true;
                    for (int iteration = 0; iteration < 32; iteration++)
                        for (nuint offset = 0; offset < 64; offset++)
                        {
                            nuint index = 63 - offset;
                            if (groups.FindAndCompress(index) != root || groups.Find(index) != root) return 3;
                            if (groups.SizeOf(index) != 64 || !groups.Connected(index, 0)) return 4;
                        }
                    deny = false;
                    if (attempts != before) return 5;
                    if (groups.Add() != 64 || groups.SetCount != 2 || groups.FindAndCompress(64) != 64) return 6;
                    if (!groups.Union(64, 0) || groups.FindAndCompress(64) != root || groups.SizeOf(64) != 65) return 7;
                    groups.Reset();
                    for (nuint index = 0; index < 65; index++)
                        if (groups.FindAndCompress(index) != index || groups.SizeOf(index) != 1) return 8;
                }
                if (allocated != released) return 9;
                return 0;
            }
            """;
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"DisjointSet compression exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void ImmutableViewsKeepTheNonmutatingQuery()
    {
        const string prefix = "using Standard::Collections;\n\nnuint Read(immutable(DisjointSet)* value) => value.";
        _ = Compiler.Check(Sources(prefix + "Find(0);"));
        Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(prefix + "FindAndCompress(0);")));
    }

    private static IEnumerable<SourceFile> Sources(string code)
        => StandardTestSources.Collections.Append(new SourceFile("disjoint-set-compression.gf", code));
}
