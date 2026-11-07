namespace gflat.Tests.Std.Time;

public sealed class TemporalComponentTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Time/Date.gf", "core/Time/DayOfWeek.gf", "core/Time/TimeOfDay.gf", "core/Time/TimeSpan.gf",
            "core/Time/DateTime.gf", "core/Time/DateTimeOffset.gf"))
            .Append(new SourceFile("temporal-components.gf", program));

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("TEMPORAL_COMPONENTS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CircularClockSubtractionMatchesTimeOnlyAndInvalidConversionsPreserveOutput(int optimization)
    {
        const long day = TimeSpan.TicksPerDay;
        var inputs = new SortedSet<long> { 0, 1, 9, 10, 9999, 10000, day - 10000, day - 10, day - 1 };
        for (int hour = 0; hour < 24; hour++)
        {
            inputs.Add(hour * TimeSpan.TicksPerHour);
            inputs.Add(hour * TimeSpan.TicksPerHour + 1234567);
        }
        var random = new Random(274819);
        for (int i = 0; i < 64; i++) inputs.Add(random.NextInt64(day));
        string path = Path.Combine(Path.GetTempPath(), "gflat_clock_subtraction_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (long left in inputs)
            foreach (long right in inputs)
            {
                writer.Write(left); writer.Write(right);
                writer.Write((new TimeOnly(left) - new TimeOnly(right)).Ticks);
            }
            foreach (long bad in new[] { long.MinValue, -1, day, day + 1, long.MaxValue })
                Assert.Throws<ArgumentOutOfRangeException>(() => TimeOnly.FromTimeSpan(new TimeSpan(bad)));
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            var result = CompilerTestHelper.Run(Sources($$"""
                using Standard;
                using Standard::Time;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                long Difference(TimeOfDay left, TimeOfDay right) nothrow => left.Subtract(right).Ticks;
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    long[3] row = default(long[3]);
                    while (true)
                    {
                        nuint read = fread(&row[0], 1, 24, file!);
                        if (read == 0) break;
                        if (read != 24) return 2;
                        TimeOfDay left = TimeOfDay::FromTicks(row[0]);
                        TimeOfDay right = TimeOfDay::FromTicks(row[1]);
                        if (Difference(left, right) != row[2]) return 3;
                    }
                    if (ferror(file!) != 0) return 4;
                    long[5] bad = [-9223372036854775807l - 1l, -1l, 864000000000l, 864000000001l, 9223372036854775807l];
                    TimeOfDay output = TimeOfDay::FromTicks(123l);
                    for (nuint i = 0; i < 5; i++)
                    {
                        bool caught = false;
                        try { output = TimeOfDay::FromTimeSpan(TimeSpan::FromTicks(bad[i])); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || output.Ticks != 123l) return 5;
                    }
                    for (int microsecond = -1; microsecond <= 1000; microsecond += 1001)
                    {
                        bool caught = false;
                        try { output = new TimeOfDay(1, 2, 3, 4, microsecond); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || output.Ticks != 123l) return 6;
                    }
                    if (new TimeOfDay(23, 59, 59, 999, 999).Ticks != 863999999990l) return 7;
                    return 0;
                }
                """), optimization);
            Assert.True(result.ExitCode == 0, $"Clock subtraction exit {result.ExitCode}: {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CivilAndOffsetComponentsMatchDotNetAtCalendarAndSubsecondBoundaries(int optimization)
    {
        var ticks = new SortedSet<long> { 0, 1, 9, 10, 9999, 10000, 9999999, 10000000,
            TimeSpan.TicksPerDay - 1, TimeSpan.TicksPerDay, DateTime.MaxValue.Ticks - 1, DateTime.MaxValue.Ticks };
        foreach (int year in new[] { 1, 4, 100, 400, 1900, 2000, 2024, 9999 })
        foreach (int month in new[] { 1, 2, 3, 12 })
        {
            ticks.Add(new DateTime(year, month, 1).Ticks);
            ticks.Add(new DateTime(year, month, DateTime.DaysInMonth(year, month), 23, 59, 59).AddTicks(9999999).Ticks);
        }
        var random = new Random(612394);
        for (int i = 0; i < 128; i++) ticks.Add(random.NextInt64(DateTime.MaxValue.Ticks));
        string path = Path.Combine(Path.GetTempPath(), "gflat_temporal_components_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (long tick in ticks)
            foreach (int offset in new[] { -840, -345, -1, 0, 1, 345, 840 })
            {
                var clock = new DateTime(tick, DateTimeKind.Unspecified);
                DateTimeOffset value;
                try { value = new DateTimeOffset(clock, TimeSpan.FromMinutes(offset)); }
                catch (ArgumentException) { continue; }
                writer.Write(tick); writer.Write(offset);
                foreach (int part in new[] { value.Year, value.Month, value.Day, value.DayOfYear, (int)value.DayOfWeek,
                    value.Hour, value.Minute, value.Second, value.Millisecond, value.Microsecond, value.Nanosecond }) writer.Write(part);
                writer.Write(DateOnly.FromDateTime(clock).DayNumber);
                writer.Write(value.TimeOfDay.Ticks);
                writer.Write(value.UtcTicks);
            }
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string[] components = ["Year", "Month", "Day", "DayOfYear", "DayOfWeek", "Hour", "Minute", "Second", "Millisecond", "Microsecond", "Nanosecond"];
            string checks = string.Join("\n", new[] { "DateTime", "DateTimeOffset" }.Select(type => $$"""
                int Check{{type}}(immutable({{type}})* value, int* expected) nothrow
                {
                    {{string.Join("\n", components.Select((name, index) => $"if ((int)value.{name} != expected[{index}]) return {index + 1};"))}}
                    return 0;
                }
                """));
            var result = CompilerTestHelper.Run(Sources($$"""
                using Standard;
                using Standard::Time;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                {{checks}}
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    long ticks = 0l;
                    int offset = 0;
                    int[11] parts = default(int[11]);
                    int dayNumber = 0;
                    long timeTicks = 0l;
                    long utcTicks = 0l;
                    while (true)
                    {
                        nuint read = fread(&ticks, 1, 8, file!);
                        if (read == 0) break;
                        if (read != 8 || fread(&offset, 4, 1, file!) != 1 || fread(&parts[0], 4, 11, file!) != 11
                            || fread(&dayNumber, 4, 1, file!) != 1 || fread(&timeTicks, 8, 1, file!) != 1 || fread(&utcTicks, 8, 1, file!) != 1) return 2;
                        immutable(DateTime) clock = DateTime::FromTicks(ticks);
                        immutable(DateTimeOffset) value = new(clock, TimeSpan::FromMinutes((long)offset));
                        int failure = CheckDateTime(&clock, parts);
                        if (failure != 0) return failure + 10;
                        failure = CheckDateTimeOffset(&value, parts);
                        if (failure != 0) return failure + 30;
                        if (value.Ticks != ticks || value.UtcTicks != utcTicks || value.Date.DayNumber != dayNumber || value.TimeOfDay.Ticks != timeTicks) return 50;
                        TimeOfDay time = new(value.Hour, value.Minute, value.Second, value.Millisecond, value.Microsecond);
                        if (time.Ticks != timeTicks - timeTicks % 10l) return 51;
                        time = TimeOfDay::FromTimeSpan(value.TimeOfDay.ToTimeSpan());
                        if (time.Ticks != timeTicks || time.Microsecond != parts[9] || time.Nanosecond != parts[10]) return 52;
                    }
                    if (ferror(file!) != 0) return 53;
                    return 0;
                }
                """), optimization);
            Assert.True(result.ExitCode == 0, $"Temporal component exit {result.ExitCode}: {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
