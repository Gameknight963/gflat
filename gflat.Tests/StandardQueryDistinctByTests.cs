namespace gflat.Tests;

public sealed class StandardQueryDistinctByTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("query-distinct-by.gf", program));

    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    private const string Memory = """
        using std; using std::Memory; using std::Queries; using std::Collections;
        extern void*? malloc(nuint bytes); extern void free(void* storage);
        int attempts = 0; int allocated = 0; int freed = 0; int denyAt = 0;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                attempts++;
                if (attempts == denyAt) return null;
                void*? result = malloc(bytes); if (result != null) allocated++; return result;
            }
            public replace void Free(void* storage) nothrow { freed++; free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FirstRowsAndEvaluationCountsMatchIndependentDistinctByOracle(int optimization)
    {
        List<(int Id, int Payload)[]> cases =
        [
            [], [(1, 10)], [(2, 20), (1, 10), (2, 99), (3, 30), (1, 88)],
            Enumerable.Range(0, 16).Select(index => (1, index)).ToArray(),
            Enumerable.Range(0, 16).Select(index => (index - 8, index)).ToArray()
        ];
        var random = new Random(761312);
        for (int sample = 0; sample < 24; sample++)
            cases.Add(Enumerable.Range(0, random.Next(0, 25))
                .Select(index => (random.Next(-4, 5), index + 10)).ToArray());
        using var packet = new MemoryStream();
        foreach (var rows in cases)
        {
            var expected = rows.DistinctBy(row => row.Id).ToArray();
            packet.WriteByte((byte)rows.Length);
            packet.WriteByte((byte)expected.Length);
            foreach (var row in rows.Concat(expected))
            {
                packet.WriteByte((byte)(row.Id + 20));
                packet.WriteByte((byte)row.Payload);
            }
        }
        string hex = Convert.ToHexString(packet.ToArray()).ToLowerInvariant();
        Run(Memory + $$$"""
            int starts = 0; int moves = 0; int reads = 0; int selections = 0;
            struct Row { public int Id; public int Payload; }
            struct Source {
                private Row* data; private nuint count;
                public Source(Row* values, nuint length) { data = values; count = length; }
                public Source::Iterator GetEnumerator() { starts++; return new Source::Iterator(data, count); }
                public struct Iterator {
                    private Row* data; private nuint count; private nuint index;
                    public Iterator(Row* values, nuint length) { data = values; count = length; index = 0; }
                    public bool MoveNext() { moves++; if (index == count) return false; index++; return true; }
                    public Row Current { get { reads++; return data[index - 1]; } }
                }
            }
            int Key(Row value) { selections++; return value.Id; }
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
            int main() {
                readonly(char)* packet = "{{{hex}}}";
                nuint position = 0;
                Row[24] rows = default(Row[24]);
                Row[24] expected = default(Row[24]);
                Row[24] saved = default(Row[24]);
                for (int sample = 0; sample < {{{cases.Count}}}; sample++) {
                    int count = Next(packet, ref position);
                    int unique = Next(packet, ref position);
                    for (int i = 0; i < count; i++) {
                        rows[i].Id = Next(packet, ref position) - 20;
                        rows[i].Payload = Next(packet, ref position);
                    }
                    for (int i = 0; i < unique; i++) {
                        expected[i].Id = Next(packet, ref position) - 20;
                        expected[i].Payload = Next(packet, ref position);
                    }
                    for (int i = 0; i < 24; i++) saved[i] = rows[i];
                    var source = new Enumerable<Row, Source, Source::Iterator>(new Source(rows, (nuint)count));
                    var query = source.DistinctBy(Key);
                    starts = 0; moves = 0; reads = 0; selections = 0;
                    int before = attempts;
                    if (query.Take(0).Count() != 0 || starts != 1 || moves != 0 || reads != 0 || selections != 0 || attempts != before) return 1;
                    starts = 0; moves = 0; reads = 0; selections = 0;
                    if (query.Count() != (nuint)unique || starts != 1 || moves != count + 1 || reads != count || selections != count) return 2;
                    for (int pass = 0; pass < 2; pass++) {
                        moves = 0; reads = 0; selections = 0;
                        {
                            var iterator = query.GetEnumerator();
                            bool invalid = false;
                            try { iterator.Current; } catch (ref Exception error) { invalid = true; }
                            if (!invalid || reads != 0) return 3;
                            int index = 0;
                            while (iterator.MoveNext()) {
                                int selectedBefore = selections;
                                Row first = iterator.Current;
                                Row again = iterator.Current;
                                if (index >= unique || first.Id != expected[index].Id || first.Payload != expected[index].Payload) return 4;
                                if (again.Id != first.Id || again.Payload != first.Payload || selections != selectedBefore) return 5;
                                index++;
                            }
                            if (index != unique || moves != count + 1 || selections != count || reads != count + unique * 2) return 6;
                            if (iterator.MoveNext() || moves != count + 1) return 7;
                            invalid = false;
                            try { iterator.Current; } catch (ref Exception error) { invalid = true; }
                            if (!invalid) return 8;
                        }
                        if (allocated != freed) return 9;
                    }
                    selections = 0; reads = 0;
                    bool any = query.Any();
                    if (any != (count != 0)) return 10;
                    if (count != 0 && (selections != 1 || reads != 1)) return 11;
                    for (int i = 0; i < 24; i++)
                        if (rows[i].Id != saved[i].Id || rows[i].Payload != saved[i].Payload) return 12;
                    {
                        var first = query.GetEnumerator();
                        var second = query.GetEnumerator();
                        if (first.MoveNext() != (count != 0)) return 14;
                        if (second.MoveNext() != (count != 0)) return 15;
                        if (count != 0) {
                            if (first.Current.Payload != expected[0].Payload || second.Current.Payload != expected[0].Payload) return 16;
                            if (first.MoveNext() != (unique > 1)) return 17;
                            if (second.Current.Payload != expected[0].Payload) return 18;
                        }
                    }
                }
                if (position != {{{hex.Length}}} || allocated != freed) return 13;
                return 0;
            }
            """, optimization);
    }

    private const string Key = """
        int keyMade = 0; int keyDead = 0; int failure = 0;
        struct Key : IHashable, IEquatable<Key> {
            public int Id; private String token;
            public Key(int id) { Id = id; token = s"key"; keyMade++; }
            public immutable nuint GetHashCode() {
                if (failure == 4 && Id == 1) throw new Exception("hash", 74);
                return 0;
            }
            public immutable bool Equals(immutable(Key)* other) {
                if (failure == 5 && other.Id == 1) throw new Exception("equal", 75);
                return Id == other.Id;
            }
            ~Key() { keyDead++; }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void KeysOwnIndependentStorageAndValueCopiesOrFreshTransfersReleaseExactlyOnce(int optimization)
    {
        Run(Memory + Key + """
            int rowCopies = 0; int rowMade = 0; int rowDead = 0;
            struct Row {
                public int Id; public String Text;
                public Row(int id, readonly(char)* text) { Id = id; Text = String::From(text); rowMade++; }
                public copy Row(immutable(Row)* source) { Id = source.Id; Text = source.Text; rowCopies++; rowMade++; }
                ~Row() { rowDead++; }
            }
            Key SelectKey(Row value) => new Key(value.Id);
            String TextKey(String value) => value.Clone();
            int freshMade = 0; int freshDead = 0;
            struct FreshRow {
                public int Id; private String token;
                public FreshRow(int id) { Id = id; token = s"fresh"; freshMade++; }
                ~FreshRow() { freshDead++; }
            }
            FreshRow Fresh(int value) => new FreshRow(value);
            Key FreshKey(FreshRow value) => new Key(value.Id % 2);
            int main() {
                {
                    Vector<Row> rows = new();
                    rows.Add(new Row(1, "first")); rows.Add(new Row(2, "second")); rows.Add(new Row(1, "later"));
                    var query = rows.AsEnumerable().DistinctBy(SelectKey);
                    int copies = rowCopies;
                    if (query.Count() != 2 || rowCopies != copies + 3 || keyMade != 3 || keyDead != 3) return 20;
                    copies = rowCopies;
                    {
                        var iterator = query.GetEnumerator();
                        if (!iterator.MoveNext() || rowCopies != copies + 1) return 21;
                        {
                            Row first = iterator.Current;
                            if (rowCopies != copies + 2 || !first.Text.Equals("first") || first.Text.Data == rows[0].Text.Data) return 22;
                            first.Text[0] = 'X';
                        }
                        if (!rows[0].Text.Equals("first") || !iterator.MoveNext() || iterator.MoveNext()) return 23;
                    }
                    if (rowMade - rowDead != 3 || keyMade != keyDead) return 24;
                    foreach (Row value in query.Take(1)) { if (!value.Text.Equals("first")) return 25; }
                    if (keyMade != keyDead || rowMade - rowDead != 3) return 26;
                }
                if (rowMade != rowDead || allocated != freed) return 27;
                {
                    Vector<String> words = new(); words.Add(s"alpha"); words.Add(s"beta"); words.Add(s"alpha");
                    var query = words.AsEnumerable().DistinctBy(TextKey);
                    var iterator = query.GetEnumerator();
                    if (!iterator.MoveNext()) return 28;
                    {
                        String first = iterator.Current;
                        if (!first.Equals("alpha") || first.Data == words[0].Data) return 29;
                        first.Clear();
                    }
                    if (!iterator.MoveNext()) return 30;
                    // The first source position is past; retained key must not borrow its storage.
                    words[0][0] = 'X';
                    if (iterator.MoveNext() || !words[0].Equals("Xlpha") || !words[2].Equals("alpha")) return 31;
                }
                if (allocated != freed) return 32;
                {
                    var query = new IntRange(0, 3).AsEnumerable().Select(Fresh).DistinctBy(FreshKey);
                    int index = 0;
                    foreach (FreshRow value in query) { if (value.Id != index) return 33; index++; }
                    if (index != 2 || freshMade != 5 || freshDead != 5 || keyMade != keyDead) return 34;
                }
                if (allocated != freed || keyMade != keyDead || rowMade != rowDead || freshMade != freshDead) return 35;
                return 0;
            }
            """, optimization);
        const string borrowed = """
            using std; using std::Memory; using std::Queries; using std::Collections;
            struct Unique {
                public int Id;
                public Unique(int id) { Id = id; }
                ~Unique() {}
            }
            int Id(Unique value) => value.Id;
            int main() {
                Vector<Unique> values = new(); values.Add(new Unique(1));
                values.AsEnumerable().DistinctBy(Id).Count();
                return 0;
            }
            """;
        Compiler.Check(Sources(borrowed.Replace(".DistinctBy(Id)", "", StringComparison.Ordinal)));
        var error = Assert.Throws<gflat.CompileExceptions.TypeCheckException>(() => Compiler.Check(Sources(borrowed)));
        Assert.Contains("Argument 1 of indirect call: cannot pass 'Unique' as 'Unique'", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CallbackCopyAndEveryMeasuredAllocationFailureCleanRetainedAndCandidateOwners(int optimization)
        => Run(Memory + Key + """
            int active = 0;
            struct Source {
                public Source::Iterator GetEnumerator() => new Source::Iterator();
                public struct Iterator {
                    private String token; private int index;
                    public Iterator() { token = s"iterator"; index = -1; active++; }
                    public bool MoveNext() {
                        index++;
                        if (failure == 1 && index == 1) throw new Exception("move", 71);
                        return index < 12;
                    }
                    public int Current {
                        get {
                            if (failure == 2 && index == 1) throw new Exception("current", 72);
                            return index;
                        }
                    }
                    ~Iterator() { active--; }
                }
            }
            Key FaultKey(int value) {
                if (failure == 3 && value == 1) throw new Exception("selector", 73);
                return new Key(value % 7);
            }
            struct CopyRow {
                public String Text;
                public CopyRow() { Text = s"row"; }
                public copy CopyRow(immutable(CopyRow)* source) {
                    Text = source.Text;
                    if (failure == 6) throw new Exception("copy", 76);
                }
            }
            Key CopyKey(CopyRow value) => new Key(0);
            int main() {
                var source = new Enumerable<int, Source, Source::Iterator>(new Source());
                var query = source.DistinctBy(FaultKey);
                for (int mode = 1; mode <= 5; mode++) {
                    failure = 0;
                    {
                        var iterator = query.GetEnumerator();
                        failure = mode; bool caught = false;
                        try { while (iterator.MoveNext()) {} }
                        catch (ref Exception error) { if (error.GetCode() != 70 + mode) return 40; caught = true; }
                        failure = 0;
                        bool invalid = false;
                        try { iterator.Current; } catch (ref Exception error) { invalid = true; }
                        if (!caught || !invalid || active != 1) return 41;
                    }
                    if (active != 0 || allocated != freed || keyMade != keyDead) return 42;
                }
                {
                    Vector<CopyRow> rows = new(); rows.Add(new CopyRow());
                    int balance = allocated - freed;
                    var copying = rows.AsEnumerable().DistinctBy(CopyKey);
                    failure = 6; bool caught = false;
                    try { copying.Count(); }
                    catch (ref Exception error) { if (error.GetCode() != 76) return 43; caught = true; }
                    failure = 0;
                    if (!caught || allocated - freed != balance || !rows[0].Text.Equals("row")) return 44;
                }
                if (allocated != freed) return 45;
                int before = attempts;
                if (query.Count() != 7) return 46;
                int requests = attempts - before;
                if (requests <= 0 || active != 0 || allocated != freed || keyMade != keyDead) return 47;
                for (int point = 1; point <= requests; point++) {
                    denyAt = attempts + point;
                    bool caught = false;
                    try { query.Count(); }
                    catch (ref BadAllocException error) { caught = true; }
                    denyAt = 0;
                    if (!caught || active != 0 || allocated != freed || keyMade != keyDead) return 48;
                }
                if (query.Count() != 7 || active != 0 || allocated != freed || keyMade != keyDead) return 49;
                return 0;
            }
            """, optimization);
}
