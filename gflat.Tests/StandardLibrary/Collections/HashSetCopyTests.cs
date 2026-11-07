namespace gflat.Tests.Std.Collections;

public sealed class HashSetCopyTests
{
    private static void Run(string program, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/HashMap.gf", "core/Collections/HashSet.gf", "core/Collections/hash_set_copy.gf"))
            .Append(new SourceFile("hash-set-copy.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Set copy exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("HASH_SET_COPY.md", 0);

    private const string Prelude = """
        using Standard;
        using Standard::Collections;
        using Standard::Memory;
        using Standard::Text;

        extern "C" void*? malloc(nuint bytes);
        extern "C" void free(void* storage);
        int attempts = 0;
        int failAt = 0;
        int live = 0;
        bool deny = false;
        bool callbacks = true;
        void*? latest = null;
        nuint latestSize = 0;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes)
            {
                attempts++;
                if (deny || attempts == failAt) return null;
                void*? result = malloc(bytes);
                if (result != null) live++;
                latest = result;
                latestSize = bytes;
                return result;
            }
            public replace void Free(void* storage) nothrow { live--; free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TombstonesBoundsAndEntireStorageOverlapAreCheckedWithoutCallbacksOrAllocation(int optimization)
        => Run(Prelude + """
            struct Key : IHashable, IEquatable<Key>
            {
                public int Id;
                public immutable nuint GetHashCode()
                {
                    if (!callbacks) throw new Exception("hash");
                    return 1;
                }
                public immutable bool Equals(immutable(Key)* other)
                {
                    if (!callbacks) throw new Exception("equal");
                    return Id == other.Id;
                }
            }
            int main()
            {
                for (nuint size = 0; size <= 12; size++)
                {
                    HashSet<Key> source = new();
                    source.Reserve(24);
                    nuint allocation = (latest!).reinterpret<nuint>();
                    nuint allocationSize = latestSize;
                    for (nuint i = 0; i < size * 2; i++)
                    {
                        Key key = new();
                        key.Id = (int)i;
                        source.Add(key);
                    }
                    for (nuint i = 0; i < size; i++)
                    {
                        Key key = new();
                        key.Id = (int)(i * 2);
                        source.Remove(&key);
                    }
                    int[12] expected;
                    nuint index = 0;
                    var iterator = source.GetEnumerator();
                    while (iterator.MoveNext()) { expected[index] = iterator.Current.Id; index++; }
                    if (index != size) return 1;
                    Key[16] output = default(Key[16]);
                    Span<Key> destination = new(&output[0], 16);
                    callbacks = false;
                    deny = true;
                    int before = attempts;
                    for (nuint length = 0; length <= 16; length++)
                    {
                        for (nuint i = 0; i < 16; i++) output[i].Id = 77;
                        bool copied = source.TryCopyTo(destination.Slice(0, length));
                        if (copied != (length >= size)) return 2;
                        for (nuint i = 0; i < 16; i++)
                        {
                            int value = 77;
                            if (copied && i < size) value = expected[i];
                            if (output[i].Id != value) return 3;
                        }
                    }
                    source.CopyTo(destination, 2);
                    for (nuint i = 0; i < size; i++) if (output[i + 2].Id != expected[i]) return 4;
                    Standard::Collections::CopyTo<Key>(&source, destination);
                    if (!Standard::Collections::TryCopyTo<Key>(&source, destination)) return 5;
                    if (attempts != before) return 6;
                    deny = false;
                    bool caught = false;
                    try { source.CopyTo(destination, 17); } catch (Exception* error) { caught = true; }
                    if (!caught) return 7;
                    if (size != 0)
                    {
                        caught = false;
                        try { source.CopyTo(destination.Slice(0, size - 1)); } catch (Exception* error) { caught = true; }
                        if (!caught) return 8;
                        for (nuint offset = 0; offset < allocationSize; offset++)
                        {
                            Span<Key> overlapping = new((allocation + offset).reinterpret<Key*>(), size);
                            caught = false;
                            try { source.TryCopyTo(overlapping); } catch (Exception* error) { caught = true; }
                            if (!caught) return 9;
                        }
                        nuint preceding = allocation - size * (nuint)sizeof(Key) + 1;
                        Span<Key> crossing = new(preceding.reinterpret<Key*>(), size);
                        caught = false;
                        try { source.TryCopyTo(crossing); } catch (Exception* error) { caught = true; }
                        if (!caught) return 10;
                        if (source.TryCopyTo(new Span<Key>(allocation.reinterpret<Key*>(), size - 1))) return 11;
                    }
                    callbacks = true;
                    for (nuint i = 0; i < size; i++)
                    {
                        Key key = new(); key.Id = expected[i];
                        if (!source.Contains(&key)) return 12;
                    }
                    if (source.Count != size) return 13;
                }
                return live;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningCopiesKeepCompletedPrefixAndPreserveFailingSlotAndSource(int optimization)
        => Run(Prelude + """
            int copies = 0;
            int failCopy = 0;
            struct Key : IHashable, IEquatable<Key>
            {
                public int Id;
                public String Text;
                public Key(int id) { Id = id; Text = s"owned"; }
                public copy Key(immutable(Key)* source)
                {
                    copies++;
                    if (copies == failCopy) throw new Exception("copy", 19);
                    Id = source.Id;
                    Text = source.Text;
                }
                public immutable nuint GetHashCode()
                {
                    if (!callbacks) throw new Exception("hash");
                    return 1;
                }
                public immutable bool Equals(immutable(Key)* other)
                {
                    if (!callbacks) throw new Exception("equal");
                    return Id == other.Id;
                }
            }
            int main()
            {
                {
                    HashSet<Key> source = new();
                    for (int i = 1; i <= 3; i++) source.Add(new Key(i));
                    int[3] expected;
                    readonly(char)*[3] original;
                    var iterator = source.GetEnumerator();
                    nuint index = 0;
                    while (iterator.MoveNext())
                    {
                        expected[index] = iterator.Current.Id;
                        original[index] = iterator.Current.Text.Data!;
                        index++;
                    }
                    for (int failure = 1; failure <= 3; failure++)
                    {
                        Key[4] output = [new Key(77), new Key(77), new Key(77), new Key(77)];
                        readonly(char)*[4] old;
                        for (nuint i = 0; i < 4; i++) old[i] = output[i].Text.Data!;
                        int baseline = live;
                        Span<Key> destination = new(&output[0], 4);
                        copies = 0; failCopy = failure; callbacks = false;
                        bool caught = false;
                        try { source.CopyTo(destination); }
                        catch (Exception* error) { caught = error.GetCode() == 19; }
                        failCopy = 0;
                        if (!caught || copies != failure || live != baseline) return 1;
                        for (nuint i = 0; i < 4; i++)
                        {
                            if (i < (nuint)(failure - 1))
                            {
                                if (output[i].Id != expected[i] || output[i].Text.Data == original[i]) return 2;
                            }
                            else if (output[i].Id != 77 || output[i].Text.Data != old[i]) return 3;
                        }
                        attempts = 0;
                        source.CopyTo(destination);
                        int observed = attempts;
                        if (observed == 0) return 4;
                        for (int allocation = 1; allocation <= observed; allocation++)
                        {
                            int[4] oldIds;
                            for (nuint i = 0; i < 4; i++)
                            {
                                old[i] = output[i].Text.Data!;
                                oldIds[i] = output[i].Id;
                            }
                            copies = 0; attempts = 0; failAt = allocation;
                            caught = false;
                            try { source.CopyTo(destination); } catch (BadAllocException* error) { caught = true; }
                            failAt = 0;
                            if (!caught || live != baseline || copies < 1 || copies > 3) return 7;
                            for (nuint i = 0; i < 4; i++)
                            {
                                if (i < (nuint)(copies - 1))
                                {
                                    if (output[i].Id != expected[i] || output[i].Text.Data == original[i]) return 8;
                                }
                                else if (output[i].Id != oldIds[i] || output[i].Text.Data != old[i]) return 9;
                            }
                        }
                        source.CopyTo(destination);
                        for (nuint i = 0; i < 3; i++)
                        {
                            if (output[i].Id != expected[i] || output[i].Text.Data == original[i]) return 5;
                            output[i].Text.Append("changed");
                        }
                        callbacks = true;
                        var check = source.GetEnumerator();
                        index = 0;
                        while (check.MoveNext())
                        {
                            if (check.Current.Id != expected[index] || check.Current.Text.Data != original[index] || !check.Current.Text.Equals("owned")) return 6;
                            index++;
                        }
                    }
                }
                return live;
            }
            """, optimization);
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OptionalCopyModulePreservesNoncopyableBorrowedSetOperations(int optimization)
        => Run(Prelude + """
            int owners = 0;
            struct Key : IHashable, IEquatable<Key>
            {
                public int Id;
                public Key(int id) { Id = id; owners++; }
                ~Key() { owners--; }
                public immutable nuint GetHashCode() => (nuint)Id;
                public immutable bool Equals(immutable(Key)* other) => Id == other.Id;
            }
            int main()
            {
                {
                    HashSet<Key> source = new();
                    source.Add(new Key(3));
                    Key query = new(3);
                    if (!source.Contains(&query) || source.Find(&query)!.Id != 3) return 1;
                    if (!source.Remove(&query) || owners != 1) return 2;
                }
                if (owners != 0) return 3;
                return live;
            }
            """, optimization);

}
