using gflat.CompileExceptions;

namespace gflat.Tests.Std.Collections;

public sealed class SpanToVectorTests
{
    private static IEnumerable<SourceFile> Sources(string program) =>
        StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load("core/Collections/span_to_vector.gf"))
            .Append(new SourceFile("span_to_vector.gf", program));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopiesOutliveStackInputsAndEmptyViewsDoNotAllocate(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard::Collections;
            using Standard::Memory;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny) return null;
                    return malloc(bytes);
                }
                public replace void Free(void* storage) nothrow => free(storage);
            }
            Vector<int> Snapshot()
            {
                int[4] values = [3, 7, 11, 19];
                Span<int> view = new(values, 4);
                return view.AsImmutable().ToVector();
            }
            int main()
            {
                Vector<int> saved = Snapshot();
                if (saved.Count != 4 || saved[0] != 3 || saved[3] != 19) return 1;
                int[5] values = [1, 2, 3, 4, 5];
                Span<int> view = new(values, 5);
                for (nuint start = 0; start <= 5; start++)
                for (nuint count = 0; count <= 5 - start; count++)
                {
                    Vector<int> copy = ToVector<int>(view.AsImmutable().Slice(start, count));
                    if (copy.Count != count) return 2;
                    for (nuint i = 0; i < count; i++)
                        if (copy[i] != (int)(start + i + 1)) return 3;
                    if (count != 0)
                    {
                        copy[0] = 99;
                        if (values[start] != (int)(start + 1)) return 4;
                    }
                }
                Span<immutable(int)> empty = new(((nuint)1).reinterpret<immutable(int)*>(), 0);
                deny = true;
                int before = attempts;
                Vector<int> none = empty.ToVector();
                if (none.Count != 0 || attempts != before) return 5;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Span ToVector exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopyFailuresReleasePrefixesAndPreserveAssignmentTargets(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    if (deny) return null;
                    void*? storage = malloc(bytes);
                    if (storage != null) live++;
                    return storage;
                }
                public replace void Free(void* storage) nothrow
                {
                    live--;
                    free(storage);
                }
            }
            int copies = 0;
            int destroyed = 0;
            int budget = -1;
            struct Item
            {
                public int Key;
                public Item(int key) { Key = key; }
                public copy Item(immutable(Item)* source)
                {
                    if (budget == 0) throw new Exception("copy failed");
                    if (budget > 0) budget--;
                    copies++;
                    Key = source.Key;
                }
                ~Item() { destroyed++; }
            }
            int main()
            {
                {
                    Vector<Item> source = new();
                    for (int i = 0; i < 3; i++) source.Add(new Item(i));
                    Vector<Item> target = new();
                    target.Add(new Item(99));
                    for (int failure = 0; failure < 3; failure++)
                    {
                        budget = failure;
                        int beforeDestroyed = destroyed;
                        int beforeLive = live;
                        bool caught = false;
                        try { target = source.AsImmutable().ToVector(); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || destroyed != beforeDestroyed + failure || live != beforeLive) return 1;
                        if (target.Count != 1 || target[0].Key != 99 || source.Count != 3) return 2;
                    }
                    budget = -1;
                    deny = true;
                    int beforeCopies = copies;
                    bool caught = false;
                    try { target = source.AsImmutable().ToVector(); }
                    catch (Exception* error) { caught = true; }
                    deny = false;
                    if (!caught || copies != beforeCopies || target[0].Key != 99) return 3;
                    {
                        Vector<Item> copied = source.AsImmutable().ToVector();
                        if (copied.Count != 3 || copied[2].Key != 2) return 4;
                        copied[0].Key = 123;
                        if (source[0].Key != 0) return 5;
                    }
                }
                if (live != 0 || destroyed != 10 || copies != 6) return 6;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Span ToVector cleanup exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void CopyingRemainsLazyAndCannotBypassNoncopyableOwnership()
    {
        const string declarations = """
            using Standard::Collections;
            using Standard::Memory;

            struct Unique
            {
                public int Value;
                public Unique(int value) { Value = value; }
                ~Unique() {}
            }
            """;
        Compiler.Check(Sources(declarations + "\nint Read() { Unique item = new(7); Span<Unique> view = new(&item, 1); return view.AsImmutable()[0].Value; }"));
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(declarations +
            "\nvoid Copy() { Unique item = new(7); Span<Unique> view = new(&item, 1); Vector<Unique> copy = view.AsImmutable().ToVector(); }")));
        Assert.Contains("copy", error.Description, StringComparison.OrdinalIgnoreCase);
    }
}
