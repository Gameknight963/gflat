using System.Globalization;
using System.Text;

namespace gflat.Tests.Std.Time;

public sealed class TemporalSpanTextTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Time/Date.gf", "core/Time/TimeOfDay.gf", "core/Time/TimeSpan.gf",
                "core/Time/DateTime.gf", "core/Time/DateTimeOffset.gf", "core/Time/DayOfWeek.gf"))
            .Append(new SourceFile("temporal-span-text.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"Temporal spans exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("TEMPORAL_SPAN_TEXT.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedParsingAndGuardedFormattingMatchDotNetAtEveryCapacity(int optimization)
    {
        var random = new Random(821994);
        var checks = new StringBuilder();
        foreach (int number in new[] { 0, 1, 59, 365, 146096, DateOnly.MaxValue.DayNumber }
            .Concat(Enumerable.Range(0, 48).Select(_ => random.Next(DateOnly.MaxValue.DayNumber + 1))))
        {
            DateOnly date = DateOnly.FromDayNumber(number);
            checks.AppendLine($"if (CheckDate(\"{date:yyyy-MM-dd}\", {number})) return 1;");
        }
        foreach (long ticks in new[] { 0L, 1, 10000, 10000000, 863999999999 }
            .Concat(Enumerable.Range(0, 48).Select(_ => random.NextInt64(864000000000))))
        {
            string text = new TimeSpan(ticks).ToString("c", CultureInfo.InvariantCulture);
            checks.AppendLine($"if (CheckTimeOfDay(\"{text}\", {ticks}l)) return 2;");
        }
        foreach (long ticks in new[] { long.MinValue, long.MinValue + 1, -864000000001, -1, 0, 1, long.MaxValue }
            .Concat(Enumerable.Range(0, 48).Select(_ => random.NextInt64(long.MinValue, long.MaxValue))))
        {
            string text = new TimeSpan(ticks).ToString("c", CultureInfo.InvariantCulture);
            checks.AppendLine($"if (CheckTimeSpan(\"{text}\", {ticks}l)) return 3;");
        }
        foreach (long ticks in new[] { 0L, 1, DateTime.MaxValue.Ticks }
            .Concat(Enumerable.Range(0, 48).Select(_ => random.NextInt64(DateTime.MaxValue.Ticks + 1))))
        {
            DateTime value = new(ticks);
            string format = "yyyy-MM-dd'T'HH:mm:ss";
            if (ticks % TimeSpan.TicksPerSecond != 0) format += ".fffffff";
            string text = value.ToString(format, CultureInfo.InvariantCulture);
            checks.AppendLine($"if (CheckDateTime(\"{text}\", {ticks}l)) return 4;");
            int minutes = ticks == 0 || ticks == DateTime.MaxValue.Ticks ? 0 : random.Next(-840, 841);
            long utc = ticks - minutes * TimeSpan.TicksPerMinute;
            if (utc < 0 || utc > DateTime.MaxValue.Ticks) minutes = 0;
            var offset = new DateTimeOffset(ticks, TimeSpan.FromMinutes(minutes));
            string suffix = minutes == 0 ? "Z" : offset.ToString("zzz", CultureInfo.InvariantCulture);
            checks.AppendLine($"if (CheckDateTimeOffset(\"{text}{suffix}\", {offset.UtcTicks}l)) return 5;");
        }
        var functions = new StringBuilder();
        foreach (string type in new[] { "Date", "TimeOfDay", "TimeSpan", "DateTime", "DateTimeOffset" })
        {
            string member = type == "Date" ? "DayNumber" : type == "DateTimeOffset" ? "UtcTicks" : "Ticks";
            functions.AppendLine($$"""
                bool Check{{type}}(readonly(char)* text, long expected) nothrow
                {
                    nuint count = 0;
                    while (text[count] != '\0') count++;
                    char[40] input;
                    input[0] = '#';
                    for (nuint i = 0; i < count; i++) input[i + 1] = text[i];
                    input[count + 1] = '#';
                    var span = new Span<readonly(char)>(&input[1], count);
                    {{type}} value = new();
                    if (!{{type}}::TryParse(span, &value) || value.{{member}} != expected) return true;
                    char[40] output;
                    nuint written = 999;
                    immutable({{type}})* view = &value;
                    for (nuint capacity = 0; capacity <= count + 1; capacity++)
                    {
                        for (nuint i = 0; i < 40; i++) output[i] = '#';
                        written = 999;
                        bool success = view.TryFormat(new Span<char>(&output[1], capacity), ref written);
                        if (success != (capacity >= count)) return true;
                        nuint wanted = 0;
                        if (success) wanted = count;
                        if (written != wanted) return true;
                        for (nuint i = 0; i < 40; i++)
                        {
                            char wantedByte = '#';
                            if (success && i >= 1 && i <= count) wantedByte = text[i - 1];
                            if (output[i] != wantedByte) return true;
                        }
                    }
                    if (view.TryFormat(new Span<char>(), ref written) || written != 0) return true;
                    // Empty and malformed counted input reset to the ordinary default.
                    {{type}} baseline = new();
                    if ({{type}}::TryParse(new Span<readonly(char)>(), &value) || !value.Equals(&baseline)) return true;
                    input[1] = '#';
                    if ({{type}}::TryParse(span, &value) || !value.Equals(&baseline)) return true;
                    for (nuint i = 0; i < count; i++) input[i + 1] = text[i];
                    input[count + 1] = '\0';
                    if ({{type}}::TryParse(new Span<readonly(char)>(&input[1], count + 1), &value)) return true;
                    return false;
                }
                """);
        }
        // Parse is throwing in its signature, so check it separately from nothrow probes.
        string parseChecks = string.Join("\n", new[] { "Date", "TimeOfDay", "TimeSpan", "DateTime", "DateTimeOffset" }
            .Select(type => $$"""
                {
                    {{type}} original = new();
                    char[40] buffer;
                    nuint count = 0;
                    if (!original.TryFormat(new Span<char>(&buffer[0], 40), ref count)) return 6;
                    {{type}} parsed = {{type}}::Parse(new Span<readonly(char)>(&buffer[0], count));
                    if (!original.Equals(&parsed)) return 7;
                }
                """));
        Run(Support + functions + $$"""
            int main()
            {
                {{checks}}
                {{parseChecks}}
                return attempts;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ParseRejectsEmptyAndInvalidViews(int optimization)
    {
        string checks = string.Join("\n", new[] { "Date", "TimeOfDay", "TimeSpan", "DateTime", "DateTimeOffset" }
            .Select(type => $$"""
                for (int form = 0; form < 2; form++)
                {
                    bool caught = false;
                    try
                    {
                        Span<readonly(char)> input = new();
                        if (form != 0) input = new Span<readonly(char)>("bad", 3);
                        {{type}} value = {{type}}::Parse(input);
                    }
                    catch (Exception* error) { caught = true; }
                    if (!caught) return 1;
                }
                """));
        Run(Support + "int main() {" + checks + "return 0; }", optimization);
    }

    private const string Support = """
        using Standard;
        using Standard::Memory;
        using Standard::Time;

        int attempts = 0;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes) { attempts++; return null; }
            public replace void Free(void* storage) nothrow { }
        }
        """;
}
