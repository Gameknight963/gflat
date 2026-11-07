namespace gflat.Tests.Std.Time;

public sealed class TemporalConstructorTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("TEMPORAL_CONSTRUCTION.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ComponentsAndOffsetValidationMatchDotNet(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_temporal_ctor_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (int[] fields in Cases())
            foreach (long offset in new[] { 0L, 600000000L, -600000000L, 504000000000L, -504000000000L,
                504600000000L, -504600000000L, 1L, -1L, long.MinValue, long.MaxValue })
            {
                bool dateValid = true, offsetValid = true;
                long ticks = 0, utc = 0;
                try { ticks = new DateTime(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6], fields[7]).Ticks; }
                catch (ArgumentException) { dateValid = false; }
                try { utc = new DateTimeOffset(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6], fields[7], new TimeSpan(offset)).UtcTicks; }
                catch (ArgumentException) { offsetValid = false; }
                foreach (int field in fields) writer.Write((long)field);
                writer.Write(offset); writer.Write(dateValid ? 1L : 0L); writer.Write(ticks); writer.Write(offsetValid ? 1L : 0L); writer.Write(utc);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
                "core/Time/Date.gf", "core/Time/DayOfWeek.gf", "core/Time/TimeSpan.gf", "core/Time/TimeOfDay.gf",
                "core/Time/DateTime.gf", "core/Time/DateTimeOffset.gf"));
            var result = CompilerTestHelper.Run(sources.Append(new SourceFile("temporal-constructors.gf", $$"""
                using Standard;
                using Standard::Time;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    long[13] row = default(long[13]);
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 104, file!);
                        if (received == 0) break;
                        if (received != 104) return 2;
                        DateTime clock = DateTime::FromTicks(123l);
                        DateTimeOffset stamp = new(clock, TimeSpan::FromTicks(0l));
                        bool caught = false;
                        try { clock = new DateTime((int)row[0], (int)row[1], (int)row[2], (int)row[3], (int)row[4], (int)row[5], (int)row[6], (int)row[7]); }
                        catch (Exception* error) { caught = true; }
                        if (row[9] == 1l) { if (caught || clock.Ticks != row[10]) return 3; }
                        else if (!caught || clock.Ticks != 123l) return 4;
                        caught = false;
                        try { stamp = new DateTimeOffset((int)row[0], (int)row[1], (int)row[2], (int)row[3], (int)row[4], (int)row[5], (int)row[6], (int)row[7], TimeSpan::FromTicks(row[8])); }
                        catch (Exception* error) { caught = true; }
                        if (row[11] == 1l) { if (caught || stamp.UtcTicks != row[12] || stamp.Ticks != row[10] || stamp.Offset.Ticks != row[8]) return 5; }
                        else if (!caught || stamp.Ticks != 123l || stamp.Offset.Ticks != 0l) return 6;
                    }
                    if (ferror(file!) != 0) return 7;
                    DateTime midnight = new(2024, 2, 29);
                    if (midnight.TimeOfDay.Ticks != 0l) return 8;
                    DateTimeOffset whole = new(2024, 2, 29, 12, 34, 56, TimeSpan::FromHours(2l));
                    DateTimeOffset millis = new(2024, 2, 29, 12, 34, 56, 789, TimeSpan::FromHours(2l));
                    if (millis.Ticks - whole.Ticks != 7890000l || whole.Second != 56) return 9;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Temporal constructors exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    private static IEnumerable<int[]> Cases()
    {
        yield return [1, 1, 1, 0, 0, 0, 0, 0];
        yield return [9999, 12, 31, 23, 59, 59, 999, 999];
        int[] leap = [2024, 2, 29, 12, 34, 56, 789, 123];
        yield return leap;
        foreach (int year in new[] { 1, 4, 100, 400, 1900, 2000, 2023, 2024, 9999 })
            yield return [year, 2, 29, 23, 59, 59, 999, 999];
        for (int field = 0; field < 8; field++)
        foreach (int bad in new[] { int.MinValue, -1, int.MaxValue, 10000 })
        {
            int[] values = (int[])leap.Clone(); values[field] = bad; yield return values;
        }
        foreach (var (field, limit) in new[] { (1, 13), (2, 32), (3, 24), (4, 60), (5, 60), (6, 1000), (7, 1000) })
        {
            int[] values = (int[])leap.Clone(); values[field] = limit; yield return values;
        }
        var random = new Random(927184);
        for (int i = 0; i < 128; i++)
        {
            int year = random.Next(1, 10000), month = random.Next(1, 13);
            yield return [year, month, random.Next(1, DateTime.DaysInMonth(year, month) + 1), random.Next(24), random.Next(60), random.Next(60), random.Next(1000), random.Next(1000)];
        }
    }
}
