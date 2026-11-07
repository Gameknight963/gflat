using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class EnumerableWriterJoinTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Text/enumerable_writer_join.gf", "core/Text/text_writer_join.gf"))
            .Append(new SourceFile("enumerable-writer-join.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"Enumerable writer join exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("ENUMERABLE_WRITER_JOIN.md", 0);

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
                    for (int form = 0; form < 4; form++)
                    {
                        String result = new();
                        ITextWriter* writer = &result;
                        if (form == 0) writer.WriteJoin(values.AsEnumerable(), &separator[0], separatorCount);
                        else if (form == 1) Standard::Text::WriteJoin(writer, values.AsEnumerable(), &separator[0], separatorCount);
                        else if (form == 2) writer.WriteJoin(values.AsEnumerable(), &separator[0]);
                        else Standard::Text::WriteJoin(writer, values.AsEnumerable(), &separator[0]);
                        if (form < 2 && !result.Equals(&counted[0], countedLength)) return 1;
                        if (form >= 2 && !result.Equals(&terminated[0], terminatedLength)) return 2;
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
    public void PartialWriterAndIteratorFailuresRetainExactPrefixesAndReleaseOwners(int optimization)
        => Run(Tracking + """
            int cursors = 0;
            int reads = 0;
            int advances = 0;
            int acquisitions = 0;
            int failure = 0;
            struct Source
            {
                public Cursor GetEnumerator()
                {
                    acquisitions++;
                    if (failure == 1) throw new Exception("acquire", 45);
                    return new();
                }
                public struct Cursor
                {
                    private int position;
                    private String token;
                    public Cursor() { position = 0; token = s"cursor"; cursors++; }
                    ~Cursor() { cursors--; }
                    public bool MoveNext()
                    {
                        advances++; position++;
                        if (failure == 2 && position == 2) throw new Exception("advance", 45);
                        return position <= 3;
                    }
                    public String Current
                    {
                        get
                        {
                            reads++;
                            if (failure == 3 && position == 2) throw new Exception("current", 45);
                            if (position == 1) return s"A";
                            if (position == 2) return new String();
                            return s"BC";
                        }
                    }
                }
            }
            struct Sink : ITextWriter
            {
                private char* data;
                private int failAt;
                private nuint partial;
                public nuint Length;
                public int Calls;
                public Sink(char* output, int fail, nuint accepted)
                {
                    data = output; failAt = fail; partial = accepted; Length = 0; Calls = 0;
                }
                public void Write(readonly(char)* text, nuint count)
                {
                    Calls++;
                    nuint accepted = count;
                    if (Calls == failAt && partial < accepted) accepted = partial;
                    for (nuint i = 0; i < accepted; i++) data[Length + i] = text[i];
                    Length += accepted;
                    if (Calls == failAt) throw new Exception("writer", 73);
                }
            }
            int main()
            {
                var values = new Enumerable<String, Source, Source::Cursor>(new Source());
                readonly(char)* expected = "A||BC";
                for (int fail = 0; fail <= 4; fail++)
                for (nuint partial = 0; partial <= 2; partial++)
                {
                    char[8] output;
                    for (nuint i = 0; i < 8; i++) output[i] = '#';
                    Sink sink = new(&output[0], fail, partial);
                    ITextWriter* writer = &sink;
                    reads = 0; advances = 0; acquisitions = 0;
                    bool caught = false;
                    try { writer.WriteJoin(values, "|"); }
                    catch (Exception* error) { if (error.GetCode() != 73) return 1; caught = true; }
                    nuint wanted = 5;
                    if (fail != 0)
                    {
                        nuint last = 1;
                        if (fail == 4) last = 2;
                        if (partial < last) last = partial;
                        wanted = (nuint)(fail - 1) + last;
                    }
                    if (caught != (fail != 0) || sink.Length != wanted || cursors != 0 || live != 0) return 2;
                    for (nuint i = 0; i < 8; i++)
                    {
                        char value = '#';
                        if (i < wanted) value = expected[i];
                        if (output[i] != value) return 3;
                    }
                    if (acquisitions != 1) return 4;
                    if (fail == 0 && (reads != 3 || advances != 4 || sink.Calls != 4)) return 5;
                }
                int observed = 0;
                for (int point = 0; point <= observed; point++)
                {
                    char[8] output;
                    Sink sink = new(&output[0], 0, 0);
                    attempts = 0; failAt = point;
                    bool caught = false;
                    try { Standard::Text::WriteJoin(&sink, values, "|", 1); }
                    catch (BadAllocException* error) { caught = true; }
                    failAt = 0;
                    if (point == 0)
                    {
                        observed = attempts;
                        if (caught || sink.Length != 5) return 9;
                    }
                    else if (!caught) return 10;
                    if (cursors != 0 || live != 0 || sink.Length > 5) return 11;
                    for (nuint i = 0; i < sink.Length; i++) if (output[i] != expected[i]) return 12;
                }
                for (int mode = 1; mode <= 3; mode++)
                {
                    failure = mode;
                    char[8] output;
                    Sink sink = new(&output[0], 0, 0);
                    bool caught = false;
                    try { Standard::Text::WriteJoin(&sink, values, "|", 1); }
                    catch (Exception* error) { if (error.GetCode() != 45) return 6; caught = true; }
                    failure = 0;
                    nuint wanted = 1;
                    if (mode == 1) wanted = 0;
                    if (!caught || sink.Length != wanted || cursors != 0 || live != 0) return 7;
                    if (wanted != 0 && output[0] != 'A') return 8;
                }
                return live;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyEntriesSkipWritesAndUnusedSeparatorsAreNeverRead(int optimization)
        => Run(Tracking + """
            struct Sink : ITextWriter
            {
                public int Calls;
                public Sink() { Calls = 0; }
                public void Write(readonly(char)* data, nuint count) { Calls++; }
            }
            int main()
            {
                {
                    Vector<String> values = new();
                    Sink sink = new();
                    ITextWriter* writer = &sink;
                    readonly(char)* absent = ((nuint)1).reinterpret<readonly(char)*>();
                    attempts = 0; failAt = 1;
                    writer.WriteJoin(values.AsEnumerable(), absent);
                    writer.WriteJoin(values.AsEnumerable(), absent, (nuint)0 - 1);
                    if (sink.Calls != 0 || attempts != 0) return 1;
                    failAt = 0;
                    values.Add(new String());
                    attempts = 0; failAt = 1;
                    writer.WriteJoin(values.AsEnumerable(), absent);
                    if (sink.Calls != 0 || attempts != 0) return 2;
                    failAt = 0;
                    values.Add(new String());
                    attempts = 0; failAt = 1;
                    writer.WriteJoin(values.AsEnumerable(), absent, 0);
                    if (sink.Calls != 0 || attempts != 0) return 3;
                    failAt = 0;
                }
                return live;
            }
            """, optimization);

    private const string Tracking = """
        using Standard;
        using Standard::Collections;
        using Standard::Queries;
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
