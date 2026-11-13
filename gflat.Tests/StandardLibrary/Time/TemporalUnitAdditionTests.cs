namespace gflat.Tests.Std.Time;

public sealed class TemporalUnitAdditionTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Time/Date.gf", "core/Time/DayOfWeek.gf", "core/Time/TimeOfDay.gf", "core/Time/TimeSpan.gf",
            "core/Time/DateTime.gf", "core/Time/DateTimeOffset.gf", "core/Time/temporal_add_units.gf"))
            .Append(new SourceFile("temporal-unit-addition.gf", program));

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("TEMPORAL_UNIT_ADDITION.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnitAdditionMatchesDotNetAndExactIntegerArithmetic(int optimization)
    {
        long[] scales = [864000000000, 36000000000, 600000000, 10000000, 10000, 10, 1];
        string path = Path.Combine(Path.GetTempPath(), "gflat_add_units_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (long ticks in new[] { 0L, 1L, 504000000000L, new DateTime(2024, 2, 29, 12, 34, 56).Ticks, DateTime.MaxValue.Ticks - 504000000000L, DateTime.MaxValue.Ticks })
            foreach (int offset in new[] { -840, 0, 345, 840 })
            {
                DateTimeOffset original;
                try { original = new DateTimeOffset(new DateTime(ticks), TimeSpan.FromMinutes(offset)); }
                catch (ArgumentException) { continue; }
                for (int unit = 0; unit < scales.Length; unit++)
                for (int exact = 0; exact < 2; exact++)
                {
                    double maximum = DateTime.MaxValue.Ticks / scales[unit];
                    double[] fractions = [0, -0.0, 1, -1, 0.125, -0.125, 1.23456789, -1.23456789, 0.9 / scales[unit], -0.9 / scales[unit], maximum, -maximum, Math.BitIncrement(maximum), double.NaN, double.PositiveInfinity, double.NegativeInfinity];
                    long[] integers = [0, 1, -1, 9007199254740993, -9007199254740993, long.MinValue, long.MaxValue];
                    int count = exact == 1 || unit == 6 ? integers.Length : fractions.Length;
                    for (int i = 0; i < count; i++)
                    {
                        bool integral = exact == 1 || unit == 6;
                        long amount = integral ? integers[i] : BitConverter.DoubleToInt64Bits(fractions[i]);
                        long expected = -1;
                        try
                        {
                            if (!integral && !double.IsFinite(fractions[i])) throw new ArgumentOutOfRangeException();
                            if (integral) expected = original.AddTicks(checked(amount * scales[unit])).Ticks;
                            else expected = unit switch
                            {
                                0 => original.AddDays(fractions[i]).Ticks,
                                1 => original.AddHours(fractions[i]).Ticks,
                                2 => original.AddMinutes(fractions[i]).Ticks,
                                3 => original.AddSeconds(fractions[i]).Ticks,
                                4 => original.AddMilliseconds(fractions[i]).Ticks,
                                5 => original.AddMicroseconds(fractions[i]).Ticks,
                                _ => throw new InvalidOperationException()
                            };
                        }
                        catch (ArgumentException) { }
                        catch (OverflowException) { }
                        foreach (long value in new[] { ticks, offset, unit, integral ? 1L : 0L, amount, expected }) writer.Write(value);
                    }
                }
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(Sources($$"""
                using Standard;
                using Standard::Time;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                extern "C" int printf(readonly(char)* format, ...);
                DateTimeOffset Apply(DateTimeOffset value, long unit, long exact, long bits)
                {
                    if (unit == 6) return value.AddTicks(bits);
                    double fraction = bits.reinterpret<ref immutable(double)>();
                    if (unit == 0) { if (exact == 1) return value.AddDays(bits); return AddDays(value, fraction); }
                    if (unit == 1) { if (exact == 1) return value.AddHours(bits); return AddHours(value, fraction); }
                    if (unit == 2) { if (exact == 1) return value.AddMinutes(bits); return AddMinutes(value, fraction); }
                    if (unit == 3) { if (exact == 1) return value.AddSeconds(bits); return AddSeconds(value, fraction); }
                    if (unit == 4) { if (exact == 1) return value.AddMilliseconds(bits); return AddMilliseconds(value, fraction); }
                    if (unit == 5) { if (exact == 1) return value.AddMicroseconds(bits); return AddMicroseconds(value, fraction); }
                    return value;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    long[6] row = default(long[6]);
                    while (true)
                    {
                        nuint read = fread(&row[0], 1, 48, file!);
                        if (read == 0) break;
                        if (read != 48) return 2;
                        DateTimeOffset original = new(DateTime::FromTicks(row[0]), TimeSpan::FromMinutes(row[1]));
                        DateTimeOffset output = original;
                        bool caught = false;
                        try { output = Apply(original, row[2], row[3], row[4]); }
                        catch (Exception* error) { caught = true; }
                        if (row[5] == -1l)
                        {
                            if (!caught || output.Ticks != original.Ticks) return 3;
                        }
                        else if (caught || output.Ticks != row[5] || output.Offset.Ticks != original.Offset.Ticks) { printf("bad %lld %lld %lld %lld %lld expected %lld actual %lld\n", row[0], row[1], row[2], row[3], row[4], row[5], output.Ticks); return 4; }
                    }
                    if (ferror(file!) != 0) return 5;
                    DateTime clock = DateTime::FromTicks(1000000000000l);
                    if (clock.AddDays(1).Ticks != 1864000000000l) return 6;
                    if (clock.AddTicks(7l).Ticks != 1000000000007l) return 7;
                    if (clock.AddDays(1l).Ticks != clock.Ticks + 864000000000l) return 8;
                    if (AddDays(clock, 0.5).Ticks != clock.Ticks + 432000000000l) return 9;
                    if (clock.AddHours(1l).Ticks != clock.Ticks + 36000000000l) return 8;
                    if (AddHours(clock, 0.5).Ticks != clock.Ticks + 18000000000l) return 9;
                    if (clock.AddMinutes(1l).Ticks != clock.Ticks + 600000000l) return 8;
                    if (AddMinutes(clock, 0.5).Ticks != clock.Ticks + 300000000l) return 9;
                    if (clock.AddSeconds(1l).Ticks != clock.Ticks + 10000000l) return 8;
                    if (AddSeconds(clock, 0.5).Ticks != clock.Ticks + 5000000l) return 9;
                    if (clock.AddMilliseconds(1l).Ticks != clock.Ticks + 10000l) return 8;
                    if (AddMilliseconds(clock, 0.5).Ticks != clock.Ticks + 5000l) return 9;
                    if (clock.AddMicroseconds(1l).Ticks != clock.Ticks + 10l) return 8;
                    if (AddMicroseconds(clock, 0.5).Ticks != clock.Ticks + 5l) return 9;
                    return 0;
                }
                """), optimization);
            Assert.True(result.ExitCode == 0, $"Unit addition exit {result.ExitCode}: {result.StandardError} {result.StandardOutput}");
        }
        finally { File.Delete(path); }
    }
}

