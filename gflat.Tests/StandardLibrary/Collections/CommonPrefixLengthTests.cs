namespace gflat.Tests.Std.Collections;

public sealed class CommonPrefixLengthTests
{
    private const string Allocator = """
        using std; using std::Text; using std::Memory; using std::Collections;
        extern void*? malloc(nuint bytes); extern void free(void* storage);
        int attempts = 0; int allocated = 0; int released = 0; bool deny = false;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                attempts++; if (deny) return null;
                void*? result = malloc(bytes); if (result != null) allocated++; return result;
            }
            public replace void Free(void* storage) nothrow { released++; free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HostPrefixesAndCustomEquivalenceMatchWithAscendingBorrowedAddressTrace(int optimization)
    {
        List<(int[] Left, int[] Right)> pairs = [([], []), ([], [1]), ([1], []), ([7], [7]), ([7], [8]),
            ([1, 2, 3], [1, 2]), ([1, 2], [1, 2, 3]), ([1, 2, 3], [9, 2, 3]),
            ([1, 2, 3], [1, 9, 3]), ([1, 2, 3], [1, 2, 9]), ([10, 11, 20], [19, 18, 21]),
            ([int.MinValue, 0, int.MaxValue], [int.MinValue, 0, int.MaxValue])];
        var random = new Random(531687);
        for (int row = 0; row < 24; row++)
        {
            int[] left = Enumerable.Range(0, random.Next(1, 25)).Select(_ => random.Next(50)).ToArray();
            int[] right = (row % 4) switch
            {
                0 => [.. left], 1 => [.. left.Take(left.Length / 2)],
                2 => [.. left, 1], _ => [.. left]
            };
            if (row % 4 == 3) right[random.Next(right.Length)] += 10;
            pairs.Add((left, right));
        }
        var packet = new List<int>();
        foreach (var (left, right) in pairs)
        {
            int exact = left.Zip(right).TakeWhile(pair => pair.First == pair.Second).Count();
            int decades = left.Zip(right).TakeWhile(pair => pair.First / 10 == pair.Second / 10).Count();
            packet.AddRange([left.Length, right.Length, exact, decades]);
            packet.AddRange(left); packet.AddRange(right);
        }
        Run(Allocator + $$"""
            int calls = 0; bool decades = false;
            immutable(int)* leftOrigin = (immutable(int)*)1; immutable(int)* rightOrigin = (immutable(int)*)1;
            bool Equal(immutable(int)* left, immutable(int)* right) {
                if (left != leftOrigin + calls || right != rightOrigin + calls) throw new Exception("wrong address order", 99);
                calls++; if (decades) return *left / 10 == *right / 10; return *left == *right;
            }
            int main() {
                int[{{packet.Count}}] packet = [{{string.Join(',', packet)}}]; nuint at = 0;
                int baseline = attempts; deny = true;
                for (int row = 0; row < {{pairs.Count}}; row++) {
                    nuint leftCount = (nuint)packet[at++]; nuint rightCount = (nuint)packet[at++];
                    nuint exact = (nuint)packet[at++]; nuint grouped = (nuint)packet[at++];
                    int[25] left; int[25] right; int[25] savedLeft; int[25] savedRight;
                    for (nuint i = 0; i < leftCount; i++) { left[i] = packet[at++]; savedLeft[i] = left[i]; }
                    for (nuint i = 0; i < rightCount; i++) { right[i] = packet[at++]; savedRight[i] = right[i]; }
                    Span<int> a = new(left, leftCount); Span<int> b = new(right, rightCount);
                    nuint shorter = leftCount; if (rightCount < shorter) shorter = rightCount;
                    leftOrigin = left; rightOrigin = right;
                    for (int mode = 0; mode < 2; mode++) {
                        decades = mode == 1; calls = 0; nuint expected = exact; if (decades) expected = grouped;
                        if (CommonPrefixLength<int>(a.AsImmutable(), b.AsImmutable(), Equal) != expected) return 1;
                        nuint compared = expected; if (expected < shorter) compared++;
                        if (calls != (int)compared) return 2;
                    }
                    if (CommonPrefixLength<int>(a.AsImmutable(), b.AsImmutable()) != exact) return 3;
                    for (nuint i = 0; i < leftCount; i++) if (left[i] != savedLeft[i]) return 4;
                    for (nuint i = 0; i < rightCount; i++) if (right[i] != savedRight[i]) return 5;
                }
                if (at != {{packet.Count}} || attempts != baseline || allocated != released) return 6;
                deny = false; return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyAliasAndOverlappingSubspansRespectShortCircuitAndOriginalExceptions(int optimization)
        => Run(Allocator + """
            int calls = 0; int failAt = -1;
            immutable(int)* leftOrigin = (immutable(int)*)1; immutable(int)* rightOrigin = (immutable(int)*)1;
            bool Equal(immutable(int)* left, immutable(int)* right) {
                if (left != leftOrigin + calls || right != rightOrigin + calls) throw new Exception("wrong address order", 99);
                int index = calls; calls++; if (index == failAt) throw new Exception("original equality", 77);
                return *left == *right;
            }
            bool Unexpected(immutable(int)* left, immutable(int)* right) { throw new Exception("empty read", 99); }
            int main() {
                Span<immutable(int)> empty = new((immutable(int)*)1, 0);
                Span<immutable(int)> defaultEmpty = new();
                int[6] data = [10, 10, 10, 20, 20, 30]; Span<int> values = new(data, 6);
                int baseline = attempts; deny = true;
                if (CommonPrefixLength<int>(empty, values.AsImmutable(), Unexpected) != 0 || CommonPrefixLength<int>(values.AsImmutable(), empty, Unexpected) != 0) return 1;
                if (CommonPrefixLength<int>(empty, values.AsImmutable()) != 0 || CommonPrefixLength<int>(values.AsImmutable(), empty) != 0) return 2;
                if (CommonPrefixLength<int>(empty, defaultEmpty, Unexpected) != 0 || CommonPrefixLength<int>(empty, defaultEmpty) != 0) return 7;
                leftOrigin = data; rightOrigin = data; calls = 0;
                if (CommonPrefixLength<int>(values.AsImmutable(), values.AsImmutable(), Equal) != 6 || calls != 6) return 3;
                leftOrigin = data; rightOrigin = &data[1]; calls = 0; failAt = 3;
                if (CommonPrefixLength<int>(values.Slice(0, 4).AsImmutable(), values.Slice(1, 4).AsImmutable(), Equal) != 2 || calls != 3) return 4;
                deny = false; if (attempts != baseline) return 5;
                leftOrigin = data; rightOrigin = data; calls = 0; failAt = 2; nuint result = 99; bool caught = false;
                try { result = CommonPrefixLength<int>(values.AsImmutable(), values.AsImmutable(), Equal); }
                catch (ref Exception error) { caught = error.GetCode() == 77 && CString::Equals(error.GetMessage(), "original equality"); }
                if (!caught || result != 99 || calls != 3 || data[2] != 10 || data[3] != 20) return 6;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersKeepTheirIdentityStorageAndDestructionCounts(int optimization)
        => Run(Allocator + """
            int made = 0; int dead = 0; int calls = 0; int failAt = -1;
            immutable(Owner)* origin = (immutable(Owner)*)1;
            struct Owner {
                public int Id; public String Token;
                public Owner(int id) { Id = id; Token = s"owner"; made++; }
                ~Owner() { dead++; }
            }
            bool Equal(immutable(Owner)* left, immutable(Owner)* right) {
                if (left != origin + calls || right != origin + calls) throw new Exception("not borrowed", 99);
                int index = calls; calls++; if (index == failAt) throw new Exception("original owner equality", 77);
                return left.Id == right.Id;
            }
            int main() {
                {
                    Vector<Owner> owners = new(); owners.Reserve(4);
                    for (int id = 0; id < 4; id++) owners.Add(new Owner(id));
                    origin = &owners[0]; nuint[4] tokens;
                    for (nuint i = 0; i < 4; i++) tokens[i] = (nuint)owners[i].Token.Data;
                    int baseline = attempts; int live = allocated - released; deny = true;
                    if (CommonPrefixLength<Owner>(owners.AsImmutable(), owners.AsImmutable(), Equal) != 4 || calls != 4) return 1;
                    deny = false; if (attempts != baseline) return 2;
                    for (int failure = 0; failure < 4; failure++) {
                        calls = 0; failAt = failure; nuint result = 99; bool caught = false;
                        try { result = CommonPrefixLength<Owner>(owners.AsImmutable(), owners.AsImmutable(), Equal); }
                        catch (ref Exception error) { caught = error.GetCode() == 77 && CString::Equals(error.GetMessage(), "original owner equality"); }
                        if (!caught || result != 99 || calls != failure + 1) return 3;
                        if (made != 4 || dead != 0 || owners.Count != 4 || allocated - released != live) return 4;
                        for (nuint i = 0; i < 4; i++)
                            if (&owners[i] != origin + i || owners[i].Id != (int)i || (nuint)owners[i].Token.Data != tokens[i] || owners[i].Token.Length != 5) return 5;
                    }
                }
                if (made != 4 || dead != 4 || allocated != released) return 6; return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DefaultStringWrapperUsesCountedContentsWithoutCopyingOrAllocation(int optimization)
        => Run(Allocator + """
            int main() {
                {
                    String[4] left = [s"a\0b", s"", s"tail", s"same"];
                    String[4] right = [s"a\0b", s"", s"TAIL", s"same"];
                    Span<String> a = new(left, 4); Span<String> b = new(right, 4);
                    nuint[4] leftData; nuint[4] rightData;
                    for (nuint i = 0; i < 4; i++) { leftData[i] = (nuint)left[i].Data; rightData[i] = (nuint)right[i].Data; }
                    int baseline = attempts; int live = allocated - released; deny = true;
                    if (CommonPrefixLength<String>(a.AsImmutable(), b.AsImmutable()) != 2) return 1;
                    if (CommonPrefixLength<String>(a.Slice(0, 2).AsImmutable(), b.AsImmutable()) != 2) return 2;
                    if (CommonPrefixLength<String>(a.AsImmutable(), a.AsImmutable()) != 4) return 3;
                    if (attempts != baseline || allocated - released != live) return 4;
                    for (nuint i = 0; i < 4; i++) if ((nuint)left[i].Data != leftData[i] || (nuint)right[i].Data != rightData[i]) return 5;
                    deny = false;
                    right[0] = s"a";
                    baseline = attempts; deny = true;
                    if (CommonPrefixLength<String>(a.AsImmutable(), b.AsImmutable()) != 0 || attempts != baseline) return 6;
                    deny = false;
                }
                if (allocated != released) return 7; return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("common-prefix-length.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"CommonPrefixLength exit {result.ExitCode}: {result.StandardError}");
    }
}
