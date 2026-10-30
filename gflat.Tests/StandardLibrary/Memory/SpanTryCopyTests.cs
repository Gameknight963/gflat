using gflat.CompileExceptions;

namespace gflat.Tests.Std.Memory;

public sealed class SpanTryCopyTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => new[] { "Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Memory/span_copy.gf",
            "Memory/Span.gf", "Collections/Vector.gf", "IStringConvertable.gf", "IHashable.gf", "IEquatable.gf",
            "IComparable.gf", "Hashing/primitive_hashing.gf", "Text/String.gf", "Text/ByteSearch.gf" }
            .Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", file))))
            .Append(new SourceFile("span-try-copy.gf", program));

    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CapacityAndOverlapMatchIndependentHostSpanOracleWithoutAllocating(int optimization)
    {
        List<(int From, int Count, int To, int Capacity)> cases =
        [
            (0, 4, 1, 3), (0, 4, 1, 7), (1, 4, 0, 8), (2, 3, 2, 3),
            (0, 8, 0, 8), (8, 0, 8, 0), (0, 0, 4, 1), (4, 4, 4, 3),
            (0, 8, 0, 7), (7, 1, 0, 0), (0, 1, 7, 1)
        ];
        var random = new Random(41823);
        for (int sample = 0; sample < 40; sample++)
        {
            int count = random.Next(0, 9);
            int from = random.Next(0, 9 - count);
            int to = random.Next(0, 9);
            cases.Add((from, count, to, random.Next(0, 9 - to)));
        }
        using var packet = new MemoryStream();
        foreach (var row in cases)
        {
            int[] expected = Enumerable.Range(0, 8).ToArray();
            bool copied = expected.AsSpan(row.From, row.Count).TryCopyTo(expected.AsSpan(row.To, row.Capacity));
            packet.WriteByte((byte)row.From); packet.WriteByte((byte)row.Count);
            packet.WriteByte((byte)row.To); packet.WriteByte((byte)row.Capacity);
            packet.WriteByte(copied ? (byte)1 : (byte)0);
            foreach (int value in expected) packet.WriteByte((byte)value);
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run($$$"""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;
            int allocations = 0;
            namespace Memory {
                public replace void*? Allocate(nuint size) { allocations++; return null; }
                public replace void Free(void* storage) nothrow {}
            }
            nuint ReadByte(readonly(char)* packet, ref nuint position) {
                int result = 0;
                for (int i = 0; i < 2; i++) {
                    char digit = packet[position]; position++;
                    int value = (int)digit - (int)'0';
                    if (digit >= 'A') value = (int)digit - (int)'A' + 10;
                    result = result * 16 + value;
                }
                return (nuint)result;
            }
            int main() {
                readonly(char)* packet = "{{{hex}}}";
                nuint position = 0;
                for (int row = 0; row < {{{cases.Count}}}; row++) {
                    nuint from = ReadByte(packet, ref position);
                    nuint count = ReadByte(packet, ref position);
                    nuint to = ReadByte(packet, ref position);
                    nuint capacity = ReadByte(packet, ref position);
                    bool expected = ReadByte(packet, ref position) != 0;
                    int[8] buffer;
                    for (nuint i = 0; i < 8; i++) buffer[i] = (int)i;
                    Span<int> values = new(buffer, 8);
                    bool actual = values.AsImmutable().Slice(from, count).TryCopyTo(values.Slice(to, capacity));
                    if (actual != expected) return 1;
                    for (nuint i = 0; i < 8; i++)
                        if (buffer[i] != (int)ReadByte(packet, ref position)) return 2;
                }
                Span<immutable(int)> unreadable = new(((nuint)1).reinterpret<immutable(int)*>(), 2);
                Span<int> empty = new();
                if (unreadable.TryCopyTo(empty)) return 3;
                Span<immutable(int)> noInput = new();
                if (!noInput.TryCopyTo(empty)) return 4;
                if (position != {{{hex.Length}}} || allocations != 0) return 5;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShortBuffersNeverCopyAndOwningCopyFailuresPropagateWithNormalPartialProgress(int optimization)
    {
        Run("""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;
            extern void*? malloc(nuint bytes); extern void free(void* storage);
            int allocated = 0; int freed = 0; int copies = 0; int made = 0; int dead = 0;
            int budget = -1; bool deny = false; bool failNext = false;
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    if (failNext) { failNext = false; return null; }
                    if (deny) return null;
                    void*? result = malloc(bytes); if (result != null) allocated++; return result;
                }
                public replace void Free(void* storage) nothrow { freed++; free(storage); }
            }
            struct Item {
                public int Id; public String Text;
                public Item(int id) { Id = id; Text = s"owned"; made++; }
                public copy Item(immutable(Item)* source) {
                    copies++; Id = source.Id; Text = source.Text;
                    if (budget == 0) throw new Exception("copy failed", 72);
                    if (budget > 0) budget--; made++;
                }
                ~Item() { dead++; }
            }
            int main() {
                {
                    Vector<Item> values = new();
                    for (int id = 0; id < 5; id++) values.Add(new Item(id));
                    Span<Item> view = values.AsSpan();
                    int balance = allocated - freed;
                    budget = 0; deny = true;
                    if (view.AsImmutable().TryCopyTo(view.Slice(1))) return 10;
                    if (copies != 0 || made != 5 || dead != 0 || allocated - freed != balance) return 11;
                    if (!view.AsImmutable().TryCopyTo(view)) return 12;
                    Span<immutable(Item)> empty = new();
                    if (!empty.TryCopyTo(view)) return 13;
                    deny = false;
                    if (copies != 0 || made != 5 || dead != 0) return 14;
                    budget = 1; bool caught = false;
                    try { view.AsImmutable().Slice(0, 4).TryCopyTo(view.Slice(1)); }
                    catch (ref Exception error) { if (error.GetCode() != 72) return 15; caught = true; }
                    if (!caught || copies != 2 || made != 6 || dead != 1 || allocated - freed != balance) return 16;
                    for (nuint i = 0; i < 4; i++) if (view[i].Id != (int)i) return 17;
                    if (view[4].Id != 3 || view[4].Text.Data == view[3].Text.Data) return 18;
                    budget = -1;
                    readonly(char)* preserved = view[1].Text.Data;
                    failNext = true; caught = false;
                    try { view.AsImmutable().Slice(0, 1).TryCopyTo(view.Slice(1, 1)); }
                    catch (ref BadAllocException error) { caught = true; }
                    if (!caught || failNext || view[1].Id != 1 || view[1].Text.Data != preserved || allocated - freed != balance) return 23;
                    if (!view.AsImmutable().Slice(1, 3).TryCopyTo(view.Slice(0, 3))) return 19;
                    if (view[0].Id != 1 || view[1].Id != 2 || view[2].Id != 3 || view[3].Id != 3 || view[4].Id != 3) return 20;
                    view[0].Text[0] = 'X';
                    if (view[1].Text[0] != 'o' || view[0].Text.Data == view[1].Text.Data) return 21;
                }
                if (made != dead || allocated != freed) return 22;
                return 0;
            }
            """, optimization);

        const string protectedOutput = """
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;
            int main() {
                int[2] data = [1, 2]; Span<int> view = new(data, 2);
                view.AsImmutable().TryCopyTo(view.AsImmutable()); return 0;
            }
            """;
        Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(protectedOutput)));
        const string noncopyable = """
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;
            struct Unique { public Unique() {} ~Unique() {} }
            int main() {
                Vector<Unique> values = new(); values.Add(new Unique());
                Span<Unique> destination = values.AsSpan();
                Span<immutable(Unique)> empty = new();
                empty.TryCopyTo(destination); return 0;
            }
            """;
        Compiler.Check(Sources(noncopyable.Replace("empty.TryCopyTo(destination);", "", StringComparison.Ordinal)));
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources(noncopyable)));
        Assert.Contains("Assignment requires a copyable value, a fresh value, or 'move value'", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImmutableDescriptorsCopyIntoSeparateInitializedScalarAndStringDestinations(int optimization)
    {
        const string program = """
            using Standard;
            using Standard::Text;
            using Standard::Memory;
            using Standard::Queries;
            using Standard::Collections;
            extern void*? malloc(nuint count);
            extern void free(void* storage);
            int attempts = 0;
            int allocated = 0;
            int released = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint count)
                {
                    attempts++;
                    void*? result = malloc(count);
                    if (result != null) allocated++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { released++; free(storage); }
            }
            bool Expected(int* output)
            {
                return output[0] == 91 && output[1] == 3 && output[2] == 5
                    && output[3] == 8 && output[4] == 92;
            }
            int main()
            {
                int[3] data = [3, 5, 8];
                int[5] output = [91, 0, 0, 0, 92];
                Span<immutable(int)> source = new(data, 3);
                immutable(Span<immutable(int)>) header = source;
                immutable(Span<immutable(int)>)* observer = &source;
                Span<int> destination = new(&output[1], 3);

                header.CopyTo(destination);
                if (!Expected(output)) return 30;
                for (nuint index = 1; index < 4; index++) output[index] = 0;
                if (!header.TryCopyTo(destination) || !Expected(output)) return 31;
                for (nuint index = 1; index < 4; index++) output[index] = 0;
                observer.CopyTo(destination);
                if (!Expected(output)) return 32;
                for (nuint index = 1; index < 4; index++) output[index] = 0;
                if (!observer.TryCopyTo(destination) || !Expected(output) || attempts != 0) return 33;

                Span<int> shortOutput = destination.Slice(0, 2);
                for (nuint index = 1; index < 4; index++) output[index] = 70 + (int)index;
                if (observer.TryCopyTo(shortOutput) || attempts != 0) return 34;
                for (nuint index = 1; index < 4; index++) if (output[index] != 70 + (int)index) return 45;
                bool caught = false;
                try { header.CopyTo(shortOutput); }
                catch (ref Exception error) { caught = true; }
                if (!caught || output[0] != 91 || output[4] != 92 || data[0] != 3 || data[1] != 5 || data[2] != 8) return 35;
                for (nuint index = 1; index < 4; index++) if (output[index] != 70 + (int)index) return 46;

                Span<immutable(int)> empty = new(((nuint)1).reinterpret<immutable(int)*>(), 0);
                immutable(Span<immutable(int)>)* emptyObserver = &empty;
                Span<int> noOutput = new();
                int before = attempts;
                emptyObserver.CopyTo(noOutput);
                if (!emptyObserver.TryCopyTo(noOutput) || attempts != before) return 36;

                {
                    Vector<String> originals = new();
                    originals.Reserve(2);
                    originals.Add(s"a\0b");
                    originals.Add(s"tail");
                    Vector<String> first = new();
                    first.Add(s"old first");
                    first.Add(s"old second");
                    Vector<String> second = new();
                    second.Add(s"replace me");
                    second.Add(s"replace me too");
                    readonly(char)* originalFirst = originals[0].Data;
                    readonly(char)* originalSecond = originals[1].Data;
                    immutable(Span<immutable(String)>) owners = originals.AsImmutable();
                    immutable(Span<immutable(String)>)* borrowedOwners = &owners;
                    owners.CopyTo(first.AsSpan());
                    if (!borrowedOwners.TryCopyTo(second.AsSpan())) return 37;
                    if (first.Count != 2 || second.Count != 2 || originals.Count != 2) return 38;
                    if (first[0].Length != 3 || second[0].Length != 3 || first[0][1] != '\0' || second[0][1] != '\0') return 39;
                    if (first[1].Length != 4 || second[1].Length != 4 || first[1][3] != 'l' || second[1][3] != 'l') return 40;
                    for (nuint index = 0; index < 2; index++)
                    {
                        if (first[index].Data == originals[index].Data || second[index].Data == originals[index].Data
                            || first[index].Data == second[index].Data) return 41;
                    }
                    first[0][0] = 'X';
                    second[1][0] = 'T';
                    if (originals[0][0] != 'a' || originals[1][0] != 't' || second[0][0] != 'a' || first[1][0] != 't') return 42;
                    if (originals[0].Data != originalFirst || originals[1].Data != originalSecond) return 43;
                }
                if (allocated != released) return 44;
                return 0;
            }
            """;
        string ir = Compiler.Emit(Sources(program));
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
