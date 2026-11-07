using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class StringAppendJoinTests
{
    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Text/string_join.gf", "core/Text/enumerable_string_join.gf",
                "core/Text/string_append_join.gf", "core/Text/string_append_join_enumerable.gf"))
            .Append(new SourceFile("string-append-join.gf", code)), optimization);
        Assert.True(result.ExitCode == 0, $"Append join exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("STRING_APPEND_JOIN.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedAndTerminatedSeparatorsMatchDotNetAcrossEmptyAndBinaryValues(int optimization)
    {
        var random = new Random(774401);
        using var packet = new MemoryStream();
        using (var writer = new BinaryWriter(packet, Encoding.UTF8, true))
        for (int row = 0; row < 128; row++)
        {
            string Bytes(int count) => new(Enumerable.Range(0, count).Select(_ => (char)random.Next(256)).ToArray());
            string[] values = Enumerable.Range(0, row % 9).Select(_ => Bytes(random.Next(21))).ToArray();
            string separator = row % 4 == 0 ? "\0!" : Bytes(row % 7);
            string terminated = separator.Split('\0')[0];
            void Write(string text)
            {
                writer.Write((ushort)text.Length);
                writer.Write(Encoding.Latin1.GetBytes(text));
            }
            writer.Write((byte)values.Length);
            foreach (string value in values) Write(value);
            Write(separator);
            Write(string.Join(separator, values));
            Write(string.Join(terminated, values));
        }
        string hex = Convert.ToHexString(packet.ToArray());
        Run(Tracking + $$"""
            int Digit(char value) { if (value <= '9') return value - '0'; return value - 'A' + 10; }
            int ReadByte(readonly(char)* data, ref nuint at)
            {
                int result = Digit(data[at]) * 16 + Digit(data[at + 1]);
                at += 2;
                return result;
            }
            nuint ReadText(readonly(char)* data, ref nuint at, char* output)
            {
                nuint count = (nuint)ReadByte(data, ref at);
                count += (nuint)ReadByte(data, ref at) * 256;
                for (nuint i = 0; i < count; i++) output[i] = (char)ReadByte(data, ref at);
                return count;
            }
            int main()
            {
                readonly(char)* data = "{{hex}}";
                nuint at = 0;
                for (int row = 0; row < 128; row++)
                {
                    Vector<String> values = new();
                    int count = ReadByte(data, ref at);
                    for (int i = 0; i < count; i++)
                    {
                        char[21] bytes;
                        nuint length = ReadText(data, ref at, &bytes[0]);
                        values.Add(String::From(&bytes[0], length));
                    }
                    char[8] separator;
                    nuint separatorCount = ReadText(data, ref at, &separator[0]);
                    separator[separatorCount] = '\0';
                    char[256] counted;
                    nuint countedLength = ReadText(data, ref at, &counted[0]);
                    char[256] terminated;
                    nuint terminatedLength = ReadText(data, ref at, &terminated[0]);
                    for (int form = 0; form < 8; form++)
                    {
                        String result = s"prefix:";
                        if (form == 0) result.AppendJoin(values.AsImmutable(), &separator[0], separatorCount);
                        else if (form == 1) Standard::Text::AppendJoin(ref result, values.AsImmutable(), &separator[0], separatorCount);
                        else if (form == 2) result.AppendJoin(values.AsEnumerable(), &separator[0], separatorCount);
                        else if (form == 3) Standard::Text::AppendJoin(ref result, values.AsEnumerable(), &separator[0], separatorCount);
                        else if (form == 4) result.AppendJoin(values.AsImmutable(), &separator[0]);
                        else if (form == 5) Standard::Text::AppendJoin(ref result, values.AsImmutable(), &separator[0]);
                        else if (form == 6) result.AppendJoin(values.AsEnumerable(), &separator[0]);
                        else Standard::Text::AppendJoin(ref result, values.AsEnumerable(), &separator[0]);
                        nuint expectedLength = countedLength;
                        readonly(char)* expected = &counted[0];
                        if (form >= 4) { expectedLength = terminatedLength; expected = &terminated[0]; }
                        if (result.Length != expectedLength + 7) return 1;
                        for (nuint i = 0; i < 7; i++) if (result[i] != "prefix:"[i]) return 2;
                        for (nuint i = 0; i < expectedLength; i++) if (result[i + 7] != expected[i]) return 3;
                    }
                    if (values.Count != (nuint)count) return 3;
                }
                return live;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryAllocationFailurePreservesBorrowedSeparatorAndDestinationStorage(int optimization)
        => Run(Tracking + """
            void Apply(String* target, Vector<String>* values, int form)
            {
                readonly(char)* separator = target.Data + 1;
                if (form == 0) target.AppendJoin(values.AsImmutable(), separator, 1);
                else if (form == 1) target.AppendJoin(values.AsImmutable(), separator);
                else if (form == 2) target.AppendJoin(values.AsEnumerable(), separator, 1);
                else target.AppendJoin(values.AsEnumerable(), separator);
            }
            int main()
            {
                {
                    Vector<String> values = new();
                    values.Add(s"first"); values.Add(new String()); values.Add(s"third");
                    for (int form = 0; form < 4; form++)
                    for (int roomy = 0; roomy < 2; roomy++)
                    {
                        int observed = 0;
                        for (int point = 0; point <= observed; point++)
                        {
                            String target = s"old";
                            if (roomy != 0) target.Reserve(64);
                            readonly(char)* before = target.Data;
                            nuint capacity = target.Capacity;
                            int baseline = live;
                            attempts = 0; failAt = point;
                            bool caught = false;
                            try { Apply(&target, &values, form); }
                            catch (Exception* error) { caught = true; }
                            failAt = 0;
                            if (point == 0)
                            {
                                observed = attempts;
                                readonly(char)* expected = "oldfirstllthird";
                                if (form % 2 != 0) expected = "oldfirstldldthird";
                                if (caught || !target.Equals(expected)) return 1;
                                if (roomy != 0 && (target.Data != before || target.Capacity != capacity)) return 2;
                            }
                            else if (!caught || !target.Equals("old") || target.Data != before || target.Capacity != capacity || live != baseline) return 3;
                            if (!values[0].Equals("first") || !values[1].IsEmpty || !values[2].Equals("third")) return 4;
                        }
                    }
                }
                return live;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SelfInputAndCallbackFailuresObserveTheOriginalDestination(int optimization)
        => Run(Tracking + """
            int cursors = 0;
            int failure = 0;
            struct Source
            {
                private immutable(String)* owner;
                public Source(immutable(String)* value) { owner = value; }
                public Cursor GetEnumerator()
                {
                    if (failure == 1) throw new Exception("acquire");
                    return new Cursor(owner);
                }
                public struct Cursor
                {
                    private immutable(String)* owner;
                    private int position;
                    public Cursor(immutable(String)* value) { owner = value; position = 0; cursors++; }
                    ~Cursor() { cursors--; }
                    public bool MoveNext()
                    {
                        position++;
                        if (failure == 2 && position == 2) throw new Exception("advance");
                        return position <= 2;
                    }
                    public String Current
                    {
                        get
                        {
                            if (failure == 3 && position == 2) throw new Exception("current");
                            return *owner;
                        }
                    }
                }
            }
            int main()
            {
                {
                    String target = s"ab";
                    readonly(char)* absent = ((nuint)1).reinterpret<readonly(char)*>();
                    target.AppendJoin(new Span<immutable(String)>(&target, 1), absent);
                    if (!target.Equals("abab")) return 1;
                    target.AppendJoin(new Span<immutable(String)>(&target, 1), absent, (nuint)0 - 1);
                    if (!target.Equals("abababab")) return 2;
                    var source = new Enumerable<String, Source, Source::Cursor>(new Source(&target));
                    for (int mode = 1; mode <= 3; mode++)
                    {
                        failure = mode;
                        readonly(char)* before = target.Data;
                        nuint capacity = target.Capacity;
                        int baseline = live;
                        bool caught = false;
                        try { target.AppendJoin(source, target.Data + 1, 2); }
                        catch (Exception* error) { caught = true; }
                        failure = 0;
                        if (!caught || cursors != 0 || live != baseline || target.Data != before || target.Capacity != capacity || !target.Equals("abababab")) return 3;
                    }
                    target.AppendJoin(source, target.Data + 1, 2);
                    // Original prefix, first original value, separator "ba", second original value.
                    if (cursors != 0 || target.Length != 26) return 4;
                    for (nuint i = 0; i < 16; i++) if (target[i] != "abababab"[i % 8]) return 5;
                    if (target[16] != 'b' || target[17] != 'a') return 6;
                    for (nuint i = 18; i < 26; i++) if (target[i] != "abababab"[(i - 18) % 8]) return 7;
                }
                return live;
            }
            """, optimization);

    private const string Tracking = """
        using Standard;
        using Standard::Collections;
        using Standard::Queries;
        using Standard::Memory;
        using Standard::Text;

        extern "C" void*? malloc(nuint bytes);
        extern "C" void free(void* storage);
        int live = 0;
        int attempts = 0;
        int failAt = 0;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes)
            {
                attempts++;
                if (attempts == failAt) return null;
                void*? result = malloc(bytes);
                if (result != null) live++;
                return result;
            }
            public replace void Free(void* storage) nothrow { live--; free(storage); }
        }
        """;
}
