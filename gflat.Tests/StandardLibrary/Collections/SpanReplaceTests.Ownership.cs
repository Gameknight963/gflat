namespace gflat.Tests.Std.Collections;

public sealed partial class SpanReplaceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningSnapshotsAndFailedCopiesPreserveTheCurrentElementAndReleaseResources(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int owners = 0;
            int remaining = -1;
            int calls = 0;
            int throwAt = -1;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            struct Item
            {
                public int Key;
                public String Text;
                public Item(int key) { Key = key; Text = s"item"; owners++; }
                public copy Item(immutable(Item)* source)
                {
                    Key = source.Key;
                    Text = source.Text;
                    if (Key == 9)
                    {
                        if (remaining == 0) throw new Exception("copy failed", 26);
                        if (remaining > 0) remaining--;
                    }
                    owners++;
                }
                ~Item() { owners--; }
            }
            bool Equal(immutable(Item)* left, immutable(Item)* right)
            {
                calls++;
                if (calls == throwAt) throw new Exception("equality failed", 27);
                return left.Key == right.Key;
            }
            int main()
            {
                for (int failure = 0; failure < 3; failure++)
                {
                    {
                        Vector<Item> values = new();
                        for (int i = 0; i < 5; i++) values.Add(new Item(1 - i % 2));
                        int before = live;
                        remaining = failure;
                        calls = 0;
                        bool caught = false;
                        try { values.AsSpan().Replace(new Item(1), new Item(9), Equal); }
                        catch (ref Exception error)
                        {
                            if (error.GetCode() != 26) return 1;
                            caught = true;
                        }
                        if (!caught || live != before || owners != 5 || calls != failure * 2 + 1) return 2;
                        for (nuint i = 0; i < 5; i++)
                        {
                            int expected = 1 - (int)i % 2;
                            if (i % 2 == 0 && i / 2 < (nuint)failure) expected = 9;
                            if (values[i].Key != expected || values[i].Text.Length != 4) return 3;
                        }
                    }
                    if (live != 0 || owners != 0) return 4;
                }
                remaining = -1;
                {
                    Vector<Item> values = new();
                    for (int i = 0; i < 5; i++) values.Add(new Item(1 - i % 2));
                    int before = live;
                    calls = 0;
                    throwAt = 3;
                    bool caught = false;
                    try { Replace<Item>(values.AsSpan(), new Item(1), new Item(9), Equal); }
                    catch (ref Exception error)
                    {
                        if (error.GetCode() != 27) return 5;
                        caught = true;
                    }
                    if (!caught || calls != 3 || live != before || owners != 5) return 6;
                    if (values[0].Key != 9 || values[1].Key != 0 || values[2].Key != 1 || values[4].Key != 1) return 7;
                    throwAt = -1;
                }
                if (live != 0 || owners != 0) return 8;
                {
                    Vector<Item> source = new();
                    Vector<Item> destination = new();
                    for (int i = 0; i < 3; i++) source.Add(new Item(i));
                    for (int i = 0; i < 4; i++) destination.Add(new Item(99));
                    int before = live;
                    source.AsImmutable().Replace(destination.AsSpan(), new Item(1), new Item(9), Equal);
                    if (live != before || owners != 7) return 9;
                    if (destination[0].Key != 0 || destination[1].Key != 9 || destination[2].Key != 2 || destination[3].Key != 99) return 10;
                    destination[0].Text.AppendLiteral("!", 1);
                    if (source[0].Text.Length != 4 || destination[0].Text.Length != 5) return 11;
                    calls = 0;
                    remaining = 0;
                    Item replacement = new(9);
                    before = live;
                    bool caught = false;
                    try { source.AsSpan().Replace(source[0], replacement, Equal); }
                    catch (ref Exception error)
                    {
                        if (error.GetCode() != 26) return 12;
                        caught = true;
                    }
                    if (!caught || calls != 0 || live != before || owners != 8 || source[0].Key != 0) return 13;
                }
                if (live != 0 || owners != 0) return 14;
                {
                    Vector<String> text = new();
                    text.Add(s"x"); text.Add(s"y"); text.Add(s"x");
                    text.AsSpan().Replace(text[0], text[1]);
                    if (text[0][0] != 'y' || text[1][0] != 'y' || text[2][0] != 'y') return 15;
                    text[0][0] = 'z';
                    if (text[1][0] != 'y' || text[2][0] != 'y') return 16;
                }
                if (live != 0) return 17;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Span owning replace exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void UnusedReplaceOperationsDoNotRequireCopyableSpanElements()
        => Compiler.Emit(Sources("""
            using Standard::Collections;
            using Standard::Memory;

            struct Owner
            {
                public int Value;
                public Owner(int value) { Value = value; }
                ~Owner() { }
            }
            int main()
            {
                Vector<Owner> values = new();
                values.Add(new Owner(42));
                Span<Owner> view = values.AsSpan();
                return view[0].Value - 42;
            }
            """));
}
