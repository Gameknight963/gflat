using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class EnumerableStringJoinTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Text/enumerable_string_join.gf"))
            .Append(new SourceFile("enumerable-string-join.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"Enumerable join exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("ENUMERABLE_STRING_JOIN.md", 0);

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
                        if (form == 0) result = values.AsEnumerable().Join(&separator[0], separatorCount);
                        else if (form == 1) result = Standard::Text::Join(values.AsEnumerable(), &separator[0], separatorCount);
                        else if (form == 2) result = values.AsEnumerable().Join(&separator[0]);
                        else result = Standard::Text::Join(values.AsEnumerable(), &separator[0]);
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
    public void EveryAllocationAndIteratorFailureCleansPartialOutputAndPreservesAssignmentTarget(int optimization)
        => Run(Tracking + """
            int cursors = 0;
            int failure = 0;
            int acquisitions = 0;
            int advances = 0;
            int reads = 0;
            struct Source
            {
                public Cursor GetEnumerator()
                {
                    acquisitions++;
                    if (failure == 1) throw new Exception("acquire");
                    return new();
                }
                public struct Cursor
                {
                    private int position;
                    private String token;
                    public Cursor() { position = 0; token = s"iterator"; cursors++; }
                    ~Cursor() { cursors--; }
                    public bool MoveNext()
                    {
                        advances++;
                        position++;
                        if (failure == 2 && position == 3) throw new Exception("advance");
                        return position <= 5;
                    }
                    public String Current
                    {
                        get
                        {
                            reads++;
                            if (failure == 3 && position == 3) throw new Exception("current");
                            return s"value";
                        }
                    }
                }
            }
            int main()
            {
                var values = new Enumerable<String, Source, Source::Cursor>(new Source());
                int observed = 0;
                for (int point = 0; point <= observed; point++)
                {
                    String output = s"old";
                    int baseline = live;
                    attempts = 0; failAt = point;
                    acquisitions = 0; advances = 0; reads = 0;
                    bool caught = false;
                    try { output = values.Join("-"); }
                    catch (Exception* error) { caught = true; }
                    failAt = 0;
                    if (point == 0)
                    {
                        observed = attempts;
                        if (caught || !output.Equals("value-value-value-value-value")) return 1;
                        if (acquisitions != 1 || advances != 6 || reads != 5) return 5;
                    }
                    else if (!caught || !output.Equals("old") || live != baseline) return 2;
                    if (cursors != 0) return 3;
                }
                for (int mode = 1; mode <= 3; mode++)
                {
                    failure = mode;
                    String output = s"old";
                    int baseline = live;
                    bool caught = false;
                    try { output = values.Join(",", 1); }
                    catch (Exception* error) { caught = true; }
                    failure = 0;
                    if (!caught || cursors != 0 || live != baseline || !output.Equals("old")) return 4;
                }
                return live;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnusedSeparatorsAreNotAccessedAndEmptyOutputNeedsNoAllocation(int optimization)
        => Run(Tracking + """
            int main()
            {
                {
                    Vector<String> values = new();
                    readonly(char)* absent = ((nuint)1).reinterpret<readonly(char)*>();
                    attempts = 0; failAt = 1;
                    String empty = values.AsEnumerable().Join(absent);
                    String counted = values.AsEnumerable().Join(absent, (nuint)0 - 1);
                    if (!empty.IsEmpty || !counted.IsEmpty || attempts != 0) return 1;
                    failAt = 0;
                    values.Add(new String());
                    attempts = 0; failAt = 1;
                    String single = values.AsEnumerable().Join(absent);
                    if (!single.IsEmpty || attempts != 0) return 2;
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
