namespace gflat.Tests.Std.Collections;

public sealed class VectorCopyTests
{
    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(
        StandardTestSources.Values, StandardTestSources.Load("core/Memory/span_copy.gf", "core/Collections/vector_copy.gf"))
        .Append(new SourceFile("vector-copy.gf", code));

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("VECTOR_COPY.md", 0);

    [Fact]
    public void OptionalCopyMethodsDoNotPreventUsingNoncopyableVectors()
        => Compiler.Emit(Sources("""
            using Standard::Collections;

            struct Owner
            {
                public int Value;
                public Owner(int value) { Value = value; }
                ~Owner() { }
            }
            int main()
            {
                Vector<Owner> values = new();
                values.Add(new Owner(7));
                values.Reserve(8);
                return values[0].Value - 7;
            }
            """));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningCopiesCleanFailedTemporariesAndKeepOtherElementsAlive(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int copies = 0;
            int failAt = -1;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    if (deny) return null;
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
                public Item(int key) { Key = key; Text = s"owned"; }
                public copy Item(immutable(Item)* source)
                {
                    copies++;
                    Key = source.Key;
                    Text = source.Text;
                    if (Key == failAt) throw new Exception("copy failed");
                }
            }
            void Copy(immutable(Vector<Item>)* source, Span<Item> target) => source.CopyTo(target);
            int main()
            {
                {
                    Vector<Item> source = new();
                    for (int i = 0; i < 3; i++) source.Add(new Item(i));
                    deny = true;
                    source.CopyTo(source.AsSpan());
                    if (source.TryCopyTo(source.AsSpan().Slice(1)) || copies != 0) return 1;
                    deny = false;
                    for (int failure = 0; failure < 3; failure++)
                    {
                        Vector<Item> target = new();
                        for (int i = 0; i < 4; i++) target.Add(new Item(99));
                        int beforeLive = live;
                        int beforeCopies = copies;
                        bool backward = (target.AsSpan().Data!).reinterpret<nuint>() > (source.AsSpan().Data!).reinterpret<nuint>();
                        failAt = failure;
                        bool caught = false;
                        try { Copy(&source, target.AsSpan()); }
                        catch (Exception* error) { caught = true; }
                        int expectedCopies = failure + 1;
                        if (backward) expectedCopies = 3 - failure;
                        if (!caught || live != beforeLive || copies != beforeCopies + expectedCopies) return 2;
                        for (nuint i = 0; i < 4; i++)
                        {
                            int expected = 99;
                            bool completed = i < (nuint)failure;
                            if (backward) completed = i > (nuint)failure && i < 3;
                            if (completed) expected = (int)i;
                            if (target[i].Key != expected || target[i].Text.Length != 5) return 3;
                        }
                        for (nuint i = 0; i < 3; i++)
                            if (source[i].Key != (int)i || source[i].Text.Length != 5) return 4;
                    }
                    failAt = -1;
                    Vector<Item> target = new();
                    for (int i = 0; i < 4; i++) target.Add(new Item(99));
                    int beforeCopies = copies;
                    Copy(&source, target.AsSpan());
                    if (copies != beforeCopies + 3 || target[3].Key != 99) return 5;
                    target[0].Text.AppendLiteral("!", 1);
                    if (source[0].Text.Length != 5 || target[0].Text.Length != 6) return 6;
                    beforeCopies = copies;
                    try { source.CopyTo(0, target.AsSpan(), 4, 1); return 7; }
                    catch (Exception* error) { }
                    if (copies != beforeCopies) return 8;
                }
                if (live != 0) return 9;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Vector owner copy exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RangeCopiesAndInvalidBoundsMatchListCopyTo(int optimization)
    {
        var packet = new List<byte>();
        for (int from = 0; from <= 9; from++)
        for (int to = 0; to <= 9; to++)
        for (int count = 0; count <= 9; count++)
        {
            int[] expected = Enumerable.Range(0, 8).ToArray();
            bool valid = true;
            try { new List<int>(expected).CopyTo(from, expected, to, count); }
            catch (ArgumentException) { valid = false; }
            packet.Add((byte)from);
            packet.Add((byte)to);
            packet.Add((byte)count);
            packet.Add((byte)(valid ? 1 : 0));
            packet.AddRange(expected.Select(value => (byte)value));
        }
        string hex = Convert.ToHexString(packet.ToArray());
        var result = CompilerTestHelper.Run(Sources($$"""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;

            int Nibble(char value)
            {
                if (value <= '9') return (int)value - (int)'0';
                return (int)value - (int)'A' + 10;
            }
            nuint Read(readonly(char)* input, ref nuint position)
            {
                int value = Nibble(input[position]) * 16 + Nibble(input[position + 1]);
                position += 2;
                return (nuint)value;
            }
            int main()
            {
                readonly(char)* packet = "{{hex}}";
                nuint position = 0;
                for (int scenario = 0; scenario < 1000; scenario++)
                {
                    nuint from = Read(packet, ref position);
                    nuint to = Read(packet, ref position);
                    nuint count = Read(packet, ref position);
                    bool valid = Read(packet, ref position) != 0;
                    Vector<int> values = new();
                    for (int i = 0; i < 8; i++) values.Add(i);
                    bool caught = false;
                    try { values.CopyTo(from, values.AsSpan(), to, count); }
                    catch (Exception* error) { caught = true; }
                    if (caught == valid || values.Count != 8) return 1;
                    for (nuint i = 0; i < 8; i++)
                        if (values[i] != (int)Read(packet, ref position)) return 2;
                }
                Vector<int> source = new();
                source.Add(4); source.Add(5);
                Vector<int> destination = new();
                for (int i = 0; i < 4; i++) destination.Add(99);
                source.CopyTo(destination.AsSpan(), 1);
                if (destination[0] != 99 || destination[1] != 4 || destination[2] != 5 || destination[3] != 99) return 3;
                if (source.TryCopyTo(destination.AsSpan().Slice(3))) return 4;
                if (destination[3] != 99) return 5;
                if (!source.TryCopyTo(destination.AsSpan())) return 6;
                if (destination[0] != 4 || destination[1] != 5 || destination[2] != 5) return 7;
                CopyTo<int>(&source, destination.AsSpan());
                CopyTo<int>(&source, destination.AsSpan(), 2);
                CopyTo<int>(&source, 1, destination.AsSpan(), 0, 1);
                if (destination[0] != 5 || destination[2] != 4 || destination[3] != 5) return 8;
                if (!TryCopyTo<int>(&source, destination.AsSpan())) return 9;
                nuint maximum = (nuint)0 - (nuint)1;
                try { source.CopyTo(maximum, destination.AsSpan(), 0, 0); return 10; }
                catch (Exception* error) { }
                try { source.CopyTo(0, destination.AsSpan(), maximum, 0); return 11; }
                catch (Exception* error) { }
                try { source.CopyTo(0, destination.AsSpan(), 0, maximum); return 12; }
                catch (Exception* error) { }
                Vector<int> empty = new();
                Span<int> nowhere = new();
                empty.CopyTo(nowhere);
                empty.CopyTo(0, nowhere, 0, 0);
                if (!empty.TryCopyTo(nowhere)) return 13;
                if (position != {{hex.Length}}) return 14;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Vector copy oracle exit {result.ExitCode}: {result.StandardError}");
    }
}
