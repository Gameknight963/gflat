namespace gflat.Tests.Std.Time;

public sealed class TimeOfDayUnitTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("CLOCK_UNIT_ADDITION.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnitAdditionsAndWrappedDaysMatchTimeOnly(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_clock_units_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (long clock in new[] { 0L, 1L, 9L, 10L, TimeSpan.TicksPerDay / 2, TimeSpan.TicksPerDay - 1 })
            for (int unit = 0; unit < 3; unit++)
            for (int kind = 0; kind < (unit == 2 ? 1 : 2); kind++)
            {
                long[] exact = [long.MinValue, long.MaxValue, -256204778, -49, -1, 0, 1, 49, 256204778];
                double[] fractions = [-1000000.125, -48.5, -1.23456789, -1, -0.125, -0.0, 0, 0.125, 1, 1.23456789, 48.5, 1000000.125];
                for (int i = 0; i < (kind == 0 ? exact.Length : fractions.Length); i++)
                {
                    bool valid = true;
                    long expected = 0;
                    int days = 0;
                    long input = kind == 0 ? exact[i] : BitConverter.DoubleToInt64Bits(fractions[i]);
                    try
                    {
                        TimeOnly value = new(clock);
                        if (kind == 0)
                        {
                            long scale = unit switch { 0 => TimeSpan.TicksPerHour, 1 => TimeSpan.TicksPerMinute, _ => 1 };
                            expected = value.Add(new TimeSpan(checked(input * scale)), out days).Ticks;
                        }
                        else if (unit == 0) expected = value.AddHours(fractions[i], out days).Ticks;
                        else expected = value.AddMinutes(fractions[i], out days).Ticks;
                    }
                    catch (OverflowException) { valid = false; }
                    foreach (long item in new[] { clock, unit, kind, input, expected, days, valid ? 1L : 0L }) writer.Write(item);
                }
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
                StandardTestSources.Load("core/Time/TimeSpan.gf", "core/Time/TimeOfDay.gf"))
                .Append(new SourceFile("clock-unit-addition.gf", $$"""
                using Standard;
                using Standard::Time;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                TimeOfDay Apply(TimeOfDay clock, long unit, long kind, long input, bool reportDays, ref int days)
                {
                    if (unit == 2)
                    {
                        if (reportDays) return clock.AddTicks(input, ref days);
                        return clock.AddTicks(input);
                    }
                    if (kind == 0)
                    {
                        if (unit == 0) { if (reportDays) return clock.AddHours(input, ref days); return clock.AddHours(input); }
                        if (reportDays) return clock.AddMinutes(input, ref days);
                        return clock.AddMinutes(input);
                    }
                    double amount = input.reinterpret<double>();
                    if (unit == 0) { if (reportDays) return clock.AddHours(amount, ref days); return clock.AddHours(amount); }
                    if (reportDays) return clock.AddMinutes(amount, ref days);
                    return clock.AddMinutes(amount);
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    long[7] row = default(long[7]);
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 56, file!);
                        if (received == 0) break;
                        if (received != 56) return 2;
                        TimeOfDay clock = TimeOfDay::FromTicks(row[0]);
                        for (int form = 0; form < 2; form++)
                        {
                            TimeOfDay result = TimeOfDay::FromTicks(123l);
                            int days = 123;
                            bool caught = false;
                            try { result = Apply(clock, row[1], row[2], row[3], form == 1, ref days); }
                            catch (Exception* error) { caught = true; }
                            if (row[6] == 0l) { if (!caught || result.Ticks != 123l || days != 123) return 3; }
                            else
                            {
                                if (caught || result.Ticks != row[4]) return 4;
                                if (form == 1 && days != (int)row[5]) return 5;
                                if (form == 0 && days != 123) return 6;
                            }
                        }
                    }
                    if (ferror(file!) != 0) return 7;
                    ulong[3] invalid = [0x7ff8000000000000ul, 0x7ff0000000000000ul, 0xfff0000000000000ul];
                    for (nuint i = 0; i < 3; i++)
                    {
                        for (int unit = 0; unit < 2; unit++)
                        {
                            TimeOfDay clock = new(12, 0);
                            int days = 123;
                            bool caught = false;
                            try
                            {
                                double amount = invalid[i].reinterpret<double>();
                                if (unit == 0) clock = clock.AddHours(amount, ref days);
                                else clock = clock.AddMinutes(amount, ref days);
                            }
                            catch (Exception* error) { caught = true; }
                            if (!caught || clock.Hour != 12 || days != 123) return 8;
                        }
                    }
                    if (new TimeOfDay(23, 0).AddHours(2).Hour != 1) return 9;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Clock units exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
