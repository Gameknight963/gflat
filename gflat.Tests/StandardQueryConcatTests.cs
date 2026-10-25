namespace gflat.Tests;

public sealed class StandardQueryConcatTests
{
    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("query-concat.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OrderReplayChainingAndLazyCurrentMatchIndependentLinqWithoutAllocating(int optimization)
    {
        var random = new Random(60619);
        var cases = new List<(int[] Left, int[] Right)> { ([], []), ([], [3]), ([1], []), ([1, 2], [3, 4]) };
        for (int sample = 0; sample < 24; sample++)
            cases.Add((Enumerable.Range(0, random.Next(0, 17)).Select(_ => random.Next(-20, 21)).ToArray(),
                Enumerable.Range(0, random.Next(0, 17)).Select(_ => random.Next(-20, 21)).ToArray()));
        using var packet = new MemoryStream();
        foreach (var (left, right) in cases)
        {
            int[] expected = left.Concat(right).ToArray();
            int[] selected = expected.Where(value => value % 2 == 0).Skip(1).Take(3).ToArray();
            packet.WriteByte((byte)left.Length);
            packet.WriteByte((byte)right.Length);
            packet.WriteByte((byte)selected.Length);
            foreach (int value in left.Concat(right).Concat(expected).Concat(selected)) packet.WriteByte((byte)(value + 50));
        }
        string hex = Convert.ToHexString(packet.ToArray()).ToLowerInvariant();
        Run($$$"""
            using std;
            int attempts = 0;
            int callbacks = 0;
            namespace Memory {
                public replace void*? Allocate(nuint bytes) { attempts++; return null; }
                public replace void Free(void* storage) nothrow {}
            }
            int Next(readonly(char)* packet, ref nuint position) nothrow {
                int result = 0;
                for (int i = 0; i < 2; i++) {
                    char digit = packet[position]; position++;
                    int value = (int)digit - (int)'0';
                    if (digit >= 'a') value = (int)digit - (int)'a' + 10;
                    result = result * 16 + value;
                }
                return result;
            }
            bool Even(int value) { callbacks++; return value % 2 == 0; }
            int Project(int value) { callbacks++; return value; }
            int main() {
                readonly(char)* packet = "{{{hex}}}";
                nuint position = 0;
                int[16] left;
                int[16] right;
                int[32] expected;
                int[3] selected;
                for (int row = 0; row < {{{cases.Count}}}; row++) {
                    int leftCount = Next(packet, ref position);
                    int rightCount = Next(packet, ref position);
                    int selectedCount = Next(packet, ref position);
                    for (int i = 0; i < leftCount; i++) left[i] = Next(packet, ref position) - 50;
                    for (int i = 0; i < rightCount; i++) right[i] = Next(packet, ref position) - 50;
                    for (int i = 0; i < leftCount + rightCount; i++) expected[i] = Next(packet, ref position) - 50;
                    for (int i = 0; i < selectedCount; i++) selected[i] = Next(packet, ref position) - 50;
                    Span<int> first = new(left, (nuint)leftCount);
                    Span<int> second = new(right, (nuint)rightCount);
                    var query = first.AsEnumerable().Concat(second.AsEnumerable());
                    for (int pass = 0; pass < 2; pass++) {
                        var iterator = query.GetEnumerator();
                        int index = 0;
                        while (iterator.MoveNext()) {
                            if (index >= leftCount + rightCount || iterator.Current != expected[index]) return 1;
                            if (iterator.Current != expected[index]) return 2;
                            index++;
                        }
                        if (index != leftCount + rightCount || iterator.MoveNext()) return 3;
                    }
                    int index = 0;
                    foreach (int value in query.Where(Even).Skip(1).Take(3)) {
                        if (index >= selectedCount || value != selected[index]) return 4;
                        index++;
                    }
                    if (index != selectedCount) return 5;
                    for (int i = 0; i < leftCount; i++) if (left[i] != expected[i]) return 6;
                    for (int i = 0; i < rightCount; i++) if (right[i] != expected[leftCount + i]) return 7;
                }
                if (position != {{{hex.Length}}}) return 8;
                int[2] prefix = [1, 2]; Span<int> values = new(prefix, 2);
                var mixed = values.AsEnumerable().Concat(new IntRange(3, 2).AsEnumerable()).Concat(new IntRange(5, 1).AsEnumerable());
                int next = 1;
                foreach (int value in mixed) { if (value != next) return 9; next++; }
                if (next != 6) return 10;
                callbacks = 0;
                var projected = values.AsEnumerable().Select(Project).Concat(new IntRange(3, 1).AsEnumerable().Select(Project));
                if (projected.Count() != 3 || projected.Take(0).Count() != 0 || callbacks != 0) return 11;
                {
                    var iterator = projected.GetEnumerator();
                    if (!iterator.MoveNext() || callbacks != 0) return 12;
                    if (iterator.Current != 1 || iterator.Current != 1 || callbacks != 2) return 13;
                }
                if (attempts != 0) return 14;
                return 0;
            }
            """, optimization);
    }

    private const string Owners = """
        using std;
        extern void*? malloc(nuint bytes); extern void free(void* storage);
        int allocated = 0; int freed = 0;
        int starts = 0; int active = 0; int created = 0; int destroyed = 0;
        int leftMoves = 0; int rightMoves = 0; int leftReads = 0; int rightReads = 0;
        int failure = 0; int madeItems = 0; int deadItems = 0;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                void*? result = malloc(bytes); if (result != null) allocated++; return result;
            }
            public replace void Free(void* storage) nothrow { freed++; free(storage); }
        }
        struct Source {
            private int tag; private int length;
            public Source(int id, int count) { tag = id; length = count; }
            public Source::Iterator GetEnumerator() {
                starts = starts * 10 + tag;
                if (failure == tag) throw new Exception("factory", 70 + tag);
                return new Source::Iterator(tag, length);
            }
            public struct Iterator {
                private String token; private int tag; private int length; private int index;
                public Iterator(int id, int count) {
                    token = s"iterator"; tag = id; length = count; index = 0; active++; created++;
                }
                public bool MoveNext() {
                    if (tag == 1) leftMoves++; else rightMoves++;
                    if (failure == tag + 2) throw new Exception("advance", 72 + tag);
                    if (index == length) return false; index++; return true;
                }
                public int Current {
                    get {
                        if (tag == 1) leftReads++; else rightReads++;
                        if (failure == 5) throw new Exception("current", 75);
                        return tag * 10 + index;
                    }
                }
                ~Iterator() { active--; destroyed++; }
            }
        }
        struct Item {
            public int Id; public String Token;
            public Item(int value) { Id = value; Token = s"item"; madeItems++; }
            ~Item() { deadItems++; }
        }
        Item Fresh(int value) => new Item(value);
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedStringsCopyIndependentlyAndFreshNoncopyableValuesTransferWithCursorCleanup(int optimization)
        => Run(Owners + """
            int main() {
                {
                    Vector<String> left = new(); left.Add(s"one"); left.Add(s"two");
                    Vector<String> right = new(); right.Add(s"three");
                    readonly(char)* leftStorage = left[0].Data;
                    readonly(char)* rightStorage = right[0].Data;
                    var query = left.AsEnumerable().Concat(right.AsEnumerable());
                    int before = allocated;
                    if (query.Count() != 3 || allocated != before) return 20;
                    {
                        var iterator = query.GetEnumerator();
                        if (!iterator.MoveNext()) return 21;
                        String first = iterator.Current;
                        String again = iterator.Current;
                        if (first.Data == again.Data || first.Data == leftStorage) return 22;
                        first[0] = 'X'; again.Clear();
                        if (!left[0].Equals("one") || !left[1].Equals("two")) return 23;
                        if (!iterator.MoveNext() || !iterator.MoveNext()) return 24;
                        String last = iterator.Current;
                        if (last.Data == rightStorage || !last.Equals("three")) return 25;
                        last[0] = 'Y';
                    }
                    if (left[0].Data != leftStorage || right[0].Data != rightStorage || !right[0].Equals("three")) return 26;
                }
                if (allocated != freed) return 27;
                {
                    var left = new Enumerable<int, Source, Source::Iterator>(new Source(1, 2));
                    var right = new Enumerable<int, Source, Source::Iterator>(new Source(2, 1));
                    var query = left.Select(Fresh).Concat(right.Select(Fresh));
                    if (starts != 0 || active != 0) return 28;
                    {
                        var iterator = query.GetEnumerator();
                        if (starts != 12 || active != 2) return 29;
                        int index = 0;
                        while (iterator.MoveNext()) {
                            if (index < 2 && rightMoves != 0) return 30;
                            Item value = iterator.Current;
                            int expected = 11 + index; if (index == 2) expected = 21;
                            if (value.Id != expected || !value.Token.Equals("item")) return 31;
                            index++;
                        }
                        if (index != 3 || leftMoves != 3 || rightMoves != 2 || active != 2) return 32;
                        if (iterator.MoveNext() || leftMoves != 3 || rightMoves != 2) return 33;
                    }
                    if (active != 0 || madeItems != 3 || deadItems != 3 || created != destroyed) return 34;
                    starts = 0; leftMoves = 0; rightMoves = 0;
                    if (query.Take(0).Count() != 0 || starts != 12 || leftMoves != 0 || rightMoves != 0 || active != 0) return 35;
                    {
                        var iterator = query.Take(1).GetEnumerator();
                        if (!iterator.MoveNext()) return 36;
                        Item value = iterator.Current;
                        if (value.Id != 11) return 36;
                    }
                    if (active != 0 || created != destroyed || madeItems != deadItems) return 37;
                }
                if (active != 0 || created != destroyed || allocated != freed || madeItems != deadItems) return 38;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FactoryAdvanceCurrentAndCopyFailuresPreserveOriginalErrorsAndReleaseOwners(int optimization)
        => Run(Owners + """
            struct CopyItem {
                public String Token;
                public CopyItem() { Token = s"source"; }
                public copy CopyItem(immutable(CopyItem)* source) {
                    Token = source.Token;
                    if (failure == 6) throw new Exception("copy", 76);
                }
            }
            int main() {
                var left = new Enumerable<int, Source, Source::Iterator>(new Source(1, 1));
                var right = new Enumerable<int, Source, Source::Iterator>(new Source(2, 1));
                var query = left.Concat(right);
                for (int mode = 1; mode <= 2; mode++) {
                    failure = mode; starts = 0; bool caught = false;
                    try { query.GetEnumerator(); }
                    catch (ref Exception error) { if (error.GetCode() != 70 + mode) return 40; caught = true; }
                    failure = 0;
                    if (!caught || active != 0 || created != destroyed || allocated != freed) return 41;
                    if (mode == 1 && starts != 1 || mode == 2 && starts != 12) return 42;
                }
                for (int mode = 3; mode <= 4; mode++) {
                    failure = 0;
                    {
                        var iterator = query.GetEnumerator();
                        bool invalid = false;
                        try { iterator.Current; } catch (ref Exception error) { invalid = true; }
                        if (!invalid) return 43;
                        if (mode == 4 && (!iterator.MoveNext() || iterator.Current != 11)) return 44;
                        failure = mode; bool caught = false;
                        try { iterator.MoveNext(); }
                        catch (ref Exception error) { if (error.GetCode() != 70 + mode) return 45; caught = true; }
                        failure = 0;
                        int moves = leftMoves + rightMoves;
                        if (!caught || iterator.MoveNext() || leftMoves + rightMoves != moves || active != 2) return 46;
                        invalid = false;
                        try { iterator.Current; } catch (ref Exception error) { invalid = true; }
                        if (!invalid) return 47;
                    }
                    if (active != 0 || created != destroyed || allocated != freed) return 48;
                }
                {
                    var iterator = query.GetEnumerator();
                    if (!iterator.MoveNext()) return 49;
                    int moves = leftMoves + rightMoves; failure = 5; bool caught = false;
                    try { iterator.Current; }
                    catch (ref Exception error) { if (error.GetCode() != 75) return 50; caught = true; }
                    failure = 0;
                    if (!caught || iterator.Current != 11 || leftMoves + rightMoves != moves) return 51;
                    if (!iterator.MoveNext()) return 52;
                    failure = 5; caught = false;
                    try { iterator.Current; }
                    catch (ref Exception error) { if (error.GetCode() != 75) return 53; caught = true; }
                    failure = 0;
                    if (!caught || iterator.Current != 21 || iterator.MoveNext()) return 54;
                }
                if (active != 0 || created != destroyed || allocated != freed) return 55;
                {
                    Vector<CopyItem> values = new(); values.Add(new CopyItem());
                    int balance = allocated - freed;
                    var copying = values.AsEnumerable().Concat(values.AsEnumerable());
                    var iterator = copying.GetEnumerator(); if (!iterator.MoveNext()) return 56;
                    failure = 6; bool caught = false;
                    try { CopyItem value = iterator.Current; }
                    catch (ref Exception error) { if (error.GetCode() != 76) return 57; caught = true; }
                    failure = 0;
                    if (!caught || allocated - freed != balance || !values[0].Token.Equals("source")) return 58;
                    CopyItem first = iterator.Current;
                    if (!first.Token.Equals("source") || first.Token.Data == values[0].Token.Data) return 59;
                }
                if (allocated != freed || active != 0 || created != destroyed) return 60;
                return 0;
            }
            """, optimization);
}
