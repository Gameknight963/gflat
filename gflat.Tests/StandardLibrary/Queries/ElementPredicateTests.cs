namespace gflat.Tests.Std.Queries;

public sealed partial class ElementPredicateTests
{
    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Queries/enumerable_elements.gf", "core/Queries/enumerable_element_predicates.gf"))
        .Append(new SourceFile("element-predicates.gf", code));

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("QUERY_ELEMENT_PREDICATES.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AllSmallSelectionsMatchLinqAndTryOutputsStayUnchanged(int optimization)
    {
        var packet = new List<byte>();
        int combinations = 1;
        int scenarios = 0;
        for (int length = 0; length <= 5; length++)
        {
            for (int pattern = 0; pattern < combinations; pattern++)
            for (int kind = 0; kind < 4; kind++)
            {
                var data = new int[length];
                int remaining = pattern;
                for (int i = 0; i < length; i++) { data[i] = remaining % 3 - 1; remaining /= 3; }
                Func<int, bool> predicate = kind switch { 0 => x => x > 0, 1 => x => x == 0, 2 => _ => false, _ => _ => true };
                packet.Add((byte)length);
                packet.Add((byte)kind);
                for (int i = 0; i < 5; i++) packet.Add((byte)((i < length ? data[i] : 0) + 1));
                foreach (var select in new Func<int>[]{ () => data.First(predicate), () => data.Last(predicate), () => data.Single(predicate) })
                {
                    bool success = true;
                    int expectedValue = 99;
                    try { expectedValue = select(); }
                    catch (InvalidOperationException) { success = false; }
                    packet.Add((byte)(success ? 1 : 0));
                    packet.Add((byte)(expectedValue + 1));
                }
                scenarios++;
            }
            combinations *= 3;
        }
        string hex = Convert.ToHexString(packet.ToArray());
        var result = CompilerTestHelper.Run(Sources($$"""
            using Standard;
            using Standard::Memory;
            using Standard::Queries;

            int kind = 0;
            bool Match(int value)
            {
                if (kind == 0) return value > 0;
                if (kind == 1) return value == 0;
                return kind == 3;
            }
            int Nibble(char value)
            {
                if (value <= '9') return (int)value - (int)'0';
                return (int)value - (int)'A' + 10;
            }
            int Read(readonly(char)* input, ref nuint position)
            {
                int value = Nibble(input[position]) * 16 + Nibble(input[position + 1]);
                position += 2;
                return value;
            }
            int main()
            {
                readonly(char)* packet = "{{hex}}";
                nuint position = 0;
                for (int scenario = 0; scenario < {{scenarios}}; scenario++)
                {
                    nuint length = (nuint)Read(packet, ref position);
                    kind = Read(packet, ref position);
                    int[5] data = default(int[5]);
                    for (nuint i = 0; i < 5; i++) data[i] = Read(packet, ref position) - 1;
                    Span<int> values = new(&data[0], length);
                    var query = values.AsEnumerable();
                    for (int operation = 0; operation < 3; operation++)
                    {
                        bool success = Read(packet, ref position) != 0;
                        int expected = Read(packet, ref position) - 1;
                        int output = 99;
                        bool caught = false;
                        try
                        {
                            if (operation == 0) output = query.First(Match);
                            else if (operation == 1) output = query.Last(Match);
                            else output = query.Single(Match);
                        }
                        catch (Exception* error) { caught = true; }
                        if (caught == success || output != expected) return 1;
                        output = 99;
                        bool found = false;
                        if (operation == 0) found = query.TryFirst(Match, ref output);
                        else if (operation == 1) found = query.TryLast(Match, ref output);
                        else found = query.TrySingle(Match, ref output);
                        if (found != success || output != expected) return 2;
                        output = 99;
                        if (operation == 0) found = TryFirst(query, Match, ref output);
                        else if (operation == 1) found = TryLast(query, Match, ref output);
                        else found = TrySingle(query, Match, ref output);
                        if (found != success || output != expected) return 3;
                        if (success)
                        {
                            if (operation == 0) output = First(query, Match);
                            else if (operation == 1) output = Last(query, Match);
                            else output = Single(query, Match);
                            if (output != expected) return 4;
                        }
                    }
                }
                if (position != {{hex.Length}}) return 5;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Predicate terminal oracle exit {result.ExitCode}: {result.StandardError}");
    }
}
