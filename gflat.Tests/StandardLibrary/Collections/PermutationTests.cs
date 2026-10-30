namespace gflat.Tests.Std.Collections;

public sealed class PermutationTests
{
    private static IEnumerable<SourceFile> Sources(string code)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("permutations.gf", code));

    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(code), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    // Enumerate positional arrangements exhaustively, deduplicate, then sort.
    // This oracle does not implement the pivot/successor permutation algorithm.
    private static int[][] UniqueArrangements(int[] input)
    {
        var distinct = new Dictionary<string, int[]>(StringComparer.Ordinal);
        int[] working = (int[])input.Clone();
        void Visit(int position)
        {
            if (position == working.Length)
            {
                distinct.TryAdd(string.Join(",", working), (int[])working.Clone());
                return;
            }
            for (int i = position; i < working.Length; i++)
            {
                (working[position], working[i]) = (working[i], working[position]);
                Visit(position + 1);
                (working[position], working[i]) = (working[i], working[position]);
            }
        }
        Visit(0);
        return distinct.Values.Order(Comparer<int[]>.Create((left, right) =>
        {
            for (int i = 0; i < left.Length; i++)
            {
                int order = left[i].CompareTo(right[i]);
                if (order != 0) return order;
            }
            return 0;
        })).ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallUniqueArrangementAdvancesOrWrapsLikeIndependentLexicographicOracle(int optimization)
    {
        int[][] corpus = [[], [0], [0, 1], [0, 1, 2], [0, 0, 1], [0, 1, 1, 2],
            [3, 1, 2, 1], [0, 0, 1, 1, 2], [0, 1, 2, 3, 4], [2, 2, 2]];
        using var packet = new MemoryStream();
        int rows = 0;
        foreach (int[] input in corpus)
        foreach (bool descending in new[] { false, true })
        {
            int[][] ordered = UniqueArrangements(input);
            if (descending) Array.Reverse(ordered);
            for (int index = 0; index < ordered.Length; index++)
            foreach (bool forward in new[] { false, true })
            {
                int next = forward ? index + 1 : index - 1;
                bool advanced = next >= 0 && next < ordered.Length;
                if (!advanced) next = forward ? 0 : ordered.Length - 1;
                packet.WriteByte((byte)input.Length);
                packet.WriteByte(forward ? (byte)1 : (byte)0);
                packet.WriteByte(descending ? (byte)1 : (byte)0);
                packet.WriteByte(advanced ? (byte)1 : (byte)0);
                foreach (int value in ordered[index]) packet.WriteByte((byte)value);
                foreach (int value in ordered[next]) packet.WriteByte((byte)value);
                rows++;
            }
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run(Memory + $$$"""
            int calls = 0;
            int Ascending(readonly(int)* a, readonly(int)* b)
            {
                calls++;
                if (*a < *b) return -2147483647 - 1;
                if (*a > *b) return 17;
                return 0;
            }
            int Descending(readonly(int)* a, readonly(int)* b)
            {
                calls++;
                if (*a > *b) return -2147483647 - 1;
                if (*a < *b) return 17;
                return 0;
            }
            int Never(readonly(int)* a, readonly(int)* b)
            {
                calls++;
                throw new Exception("unexpected comparison");
            }
            nuint ReadByte(readonly(char)* text, ref nuint offset)
            {
                nuint value = 0;
                for (int i = 0; i < 2; i++)
                {
                    char digit = text[offset];
                    offset++;
                    nuint part = (nuint)((int)digit - (int)'0');
                    if (digit >= 'A') part = (nuint)((int)digit - (int)'A' + 10);
                    value = value * 16 + part;
                }
                return value;
            }
            int main()
            {
                readonly(char)* packet = "{{{hex}}}";
                nuint offset = 0;
                int[7] storage;
                for (int row = 0; row < {{{rows}}}; row++)
                {
                    nuint length = ReadByte(packet, ref offset);
                    bool forward = ReadByte(packet, ref offset) != 0;
                    bool descending = ReadByte(packet, ref offset) != 0;
                    bool expected = ReadByte(packet, ref offset) != 0;
                    for (nuint i = 0; i < 7; i++) storage[i] = -91;
                    for (nuint i = 0; i < length; i++)
                        storage[i + 1] = (int)ReadByte(packet, ref offset);
                    Span<int> values = new(&storage[1], length);
                    int(readonly(int)*, readonly(int)*)* compare = Ascending;
                    if (descending) compare = Descending;
                    int oldAttempts = attempts;
                    int oldCalls = calls;
                    deny = true;
                    bool advanced = false;
                    if (forward) advanced = NextPermutation<int>(values, compare);
                    else advanced = PreviousPermutation<int>(values, compare);
                    deny = false;
                    if (advanced != expected || attempts != oldAttempts) return 1;
                    if (length < 2 && calls != oldCalls) return 2;
                    for (nuint i = 0; i < length; i++)
                        if (values[i] != (int)ReadByte(packet, ref offset)) return 3;
                    for (nuint i = 0; i < 7; i++)
                        if ((i == 0 || i > length) && storage[i] != -91) return 4;
                }
                if (offset != {{{hex.Length}}}) return 5;
                int before = calls;
                Span<int> empty = new();
                Span<int> unread = new(((nuint)4096).reinterpret<int*>(), 1);
                if (NextPermutation<int>(empty, Never) || PreviousPermutation<int>(empty, Never)) return 6;
                if (NextPermutation<int>(unread, Never) || PreviousPermutation<int>(unread, Never)
                    || calls != before) return 7;
                if (allocated != released) return 8;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersRelocateAndEveryComparatorFailureKeepsOriginalArrangement(int optimization)
        => Run(Memory + """
            int dead = 0;
            int calls = 0;
            int throwAt = 0;
            bool bad = false;
            Vector<Owner>*? watched = null;
            struct Owner
            {
                public int Id;
                public int* Resource;
                public Owner(int id) { Id = id; Resource = new* int(id); }
                ~Owner() { dead++; delete Resource; }
            }
            bool Intact(immutable(Vector<Owner>)* values)
            {
                bool[6] seen;
                for (nuint i = 0; i < 6; i++) seen[i] = false;
                for (nuint i = 0; i < values.Count; i++)
                {
                    int id = (*values)[i].Id;
                    if (id < 0 || id >= 6) return false;
                    if (seen[(nuint)id] || *(*values)[i].Resource != id) return false;
                    seen[(nuint)id] = true;
                }
                return values.Count == 6;
            }
            int Compare(immutable(Owner)* a, immutable(Owner)* b)
            {
                if (!Intact(watched!)) bad = true;
                calls++;
                if (calls == throwAt) throw new Exception("comparison failed", 37);
                int left = a.Id % 3;
                int right = b.Id % 3;
                if (left < right) return -2147483647 - 1;
                if (left > right) return 17;
                return 0;
            }
            void Fill(ref Vector<Owner> values, bool descending)
            {
                for (nuint i = 0; i < 6; i++)
                {
                    nuint source = i;
                    if (descending) source = 5 - i;
                    values.Add(new Owner((int)((source % 2) * 3 + source / 2)));
                }
            }
            bool Step(Span<Owner> values, bool forward)
            {
                if (forward) return NextPermutation<Owner>(values, Compare);
                return PreviousPermutation<Owner>(values, Compare);
            }
            int main()
            {
                int[4] totals;
                int[6][4] expected = [
                    [0, 0, 1, 2, 1, 2], [2, 2, 1, 0, 1, 0],
                    [0, 0, 1, 1, 2, 2], [2, 2, 1, 1, 0, 0]];
                for (nuint mode = 0; mode < 4; mode++)
                {
                    bool forward = mode % 2 == 0;
                    bool descending = mode == 1 || mode == 2;
                    int oldDead = dead;
                    {
                        Vector<Owner> values = new();
                        Fill(ref values, descending);
                        watched = &values;
                        calls = 0;
                        int oldAttempts = attempts;
                        deny = true;
                        bool advanced = Step(values.AsSpan(), forward);
                        deny = false;
                        if (advanced != (mode < 2) || bad || dead != oldDead
                            || !Intact(&values) || attempts != oldAttempts) return 1;
                        for (nuint i = 0; i < 6; i++)
                            if (values[i].Id % 3 != expected[mode][i]) return 2;
                        totals[mode] = calls;
                    }
                    if (dead != oldDead + 6 || allocated != released) return 3;
                    for (int point = 1; point <= totals[mode]; point++)
                    {
                        oldDead = dead;
                        {
                            Vector<Owner> values = new();
                            Fill(ref values, descending);
                            watched = &values;
                            calls = 0;
                            throwAt = point;
                            int[6] saved;
                            for (nuint i = 0; i < 6; i++) saved[i] = values[i].Id;
                            bool caught = false;
                            try { Step(values.AsSpan(), forward); }
                            catch (ref Exception error)
                            {
                                if (error.GetCode() != 37) return 4;
                                caught = true;
                            }
                            throwAt = 0;
                            if (!caught || bad || dead != oldDead || !Intact(&values)) return 5;
                            for (nuint i = 0; i < 6; i++)
                                if (values[i].Id != saved[i]) return 6;
                        }
                        if (dead != oldDead + 6 || allocated != released) return 7;
                    }
                }
                return 0;
            }
            """, optimization);

    private const string Memory = """
        using Standard; using Standard::Memory; using Standard::Collections;
        int attempts = 0;
        int allocated = 0;
        int released = 0;
        bool deny = false;
        extern void*? malloc(nuint count);
        extern void free(void* value);
        namespace Memory
        {
            public replace void*? Allocate(nuint count)
            {
                attempts++;
                if (deny) return null;
                void*? value = malloc(count);
                if (value != null) allocated++;
                return value;
            }
            public replace void Free(void* value) nothrow { released++; free(value); }
        }
        """;
}
