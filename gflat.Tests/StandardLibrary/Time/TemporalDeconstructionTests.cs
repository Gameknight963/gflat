namespace gflat.Tests.Std.Time;

public sealed class TemporalDeconstructionTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("TEMPORAL_DECONSTRUCTION.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GregorianCycleAndClockComponentsMatchDotNetWithoutAllocation(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_deconstruction_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                void Write(int day, long ticks)
                {
                    var date = DateOnly.FromDayNumber(day);
                    var clock = new TimeOnly(ticks);
                    writer.Write(day); writer.Write(ticks);
                    writer.Write(date.Year); writer.Write(date.Month); writer.Write(date.Day);
                    writer.Write(clock.Hour); writer.Write(clock.Minute); writer.Write(clock.Second);
                    writer.Write(clock.Millisecond); writer.Write(clock.Microsecond);
                }
                for (int day = 0; day < 146097; day++)
                    Write(day, (day % 86400) * TimeSpan.TicksPerSecond + day * 7919L % TimeSpan.TicksPerSecond);
                Write(DateOnly.MaxValue.DayNumber, TimeSpan.TicksPerDay - 1);
                Write(0, 0);
                var random = new Random(731982);
                for (int i = 0; i < 4096; i++)
                    Write(random.Next(DateOnly.MaxValue.DayNumber + 1), random.NextInt64(TimeSpan.TicksPerDay));
            }
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string code = $$"""
                using Standard::Time;

                extern "C" void*? malloc(nuint bytes);
                extern "C" void free(void* storage);
                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode) nothrow;
                extern "C" nuint fread(void* data, nuint size, nuint count, void* stream) nothrow;
                extern "C" int fclose(void* stream) nothrow;
                extern "C" int ferror(void* stream) nothrow;
                bool deny = false;
                int attempts = 0;
                namespace Memory
                {
                    public replace void*? Allocate(nuint bytes)
                    {
                        attempts++;
                        if (deny) return null;
                        return malloc(bytes);
                    }
                    public replace void Free(void* storage) nothrow => free(storage);
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    int day = 0;
                    long ticks = 0l;
                    int[8] parts;
                    while (true)
                    {
                        nuint read = fread(&day, 1, 4, file!);
                        if (read == 0) break;
                        if (read != 4 || fread(&ticks, 1, 8, file!) != 8 || fread(&parts[0], 4, 8, file!) != 8) return 2;
                        Date date = Date::FromDayNumber(day);
                        TimeOfDay clock = TimeOfDay::FromTicks(ticks);
                        int year = -1;
                        int month = -1;
                        int dateDay = -1;
                        int hour = -1;
                        int minute = -1;
                        int second = -1;
                        int millisecond = -1;
                        int microsecond = -1;
                        int before = attempts;
                        deny = true;
                        date.Deconstruct(ref year, ref month, ref dateDay);
                        if (year != parts[0] || month != parts[1] || dateDay != parts[2]) return 3;
                        clock.Deconstruct(ref hour, ref minute);
                        if (hour != parts[3] || minute != parts[4]) return 4;
                        clock.Deconstruct(ref hour, ref minute, ref second);
                        if (hour != parts[3] || minute != parts[4] || second != parts[5]) return 5;
                        clock.Deconstruct(ref hour, ref minute, ref second, ref millisecond);
                        if (hour != parts[3] || minute != parts[4] || second != parts[5] || millisecond != parts[6]) return 6;
                        clock.Deconstruct(ref hour, ref minute, ref second, ref millisecond, ref microsecond);
                        if (hour != parts[3] || minute != parts[4] || second != parts[5] || millisecond != parts[6] || microsecond != parts[7]) return 7;
                        int shared = -1;
                        date.Deconstruct(ref shared, ref shared, ref shared);
                        if (shared != parts[2]) return 8;
                        clock.Deconstruct(ref shared, ref shared, ref shared, ref shared, ref shared);
                        if (shared != parts[7]) return 9;
                        deny = false;
                        if (attempts != before || date.DayNumber != day || clock.Ticks != ticks) return 10;
                    }
                    return ferror(file!);
                }
                """;
            var sources = StandardTestSources.Time.Append(new SourceFile("temporal-deconstruction.gf", code));
            var result = CompilerTestHelper.Run(sources, optimization);
            Assert.True(result.ExitCode == 0, $"Deconstruction exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
