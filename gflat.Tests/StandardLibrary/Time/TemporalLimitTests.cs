namespace gflat.Tests.Std.Time;

public sealed class TemporalLimitTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("TEMPORAL_LIMITS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LimitsMatchDotNetWithoutAllocatingAndReturnedValuesAreIndependent(int optimization)
    {
        string program = $$"""
            using Standard;
            using Standard::Time;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            bool deny = false;
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
            bool Observe() nothrow
            {
                const long second = TimeSpan::TicksPerSecond;
                if (second != {{Literal(TimeSpan.TicksPerSecond)}} || TimeSpan::NanosecondsPerTick != {{Literal(TimeSpan.NanosecondsPerTick)}}) return false;
                if (TimeSpan::TicksPerMicrosecond != {{Literal(TimeSpan.TicksPerMicrosecond)}} || TimeSpan::TicksPerMillisecond != {{Literal(TimeSpan.TicksPerMillisecond)}}) return false;
                if (TimeSpan::TicksPerMinute != {{Literal(TimeSpan.TicksPerMinute)}} || TimeSpan::TicksPerHour != {{Literal(TimeSpan.TicksPerHour)}} || TimeSpan::TicksPerDay != {{Literal(TimeSpan.TicksPerDay)}}) return false;
                TimeSpan zero = TimeSpan::Zero;
                TimeSpan minimum = TimeSpan::MinValue;
                TimeSpan maximum = TimeSpan::MaxValue;
                if (zero.Ticks != 0l || minimum.Ticks != {{Literal(TimeSpan.MinValue.Ticks)}} || maximum.Ticks != {{Literal(TimeSpan.MaxValue.Ticks)}}) return false;
                Date first = Date::MinValue;
                Date last = Date::MaxValue;
                if (first.DayNumber != {{DateOnly.MinValue.DayNumber}} || last.DayNumber != {{DateOnly.MaxValue.DayNumber}}) return false;
                if (last.Year != 9999 || last.Month != 12 || last.Day != 31 || first.Year != 1 || first.Month != 1 || first.Day != 1) return false;
                TimeOfDay midnight = TimeOfDay::MinValue;
                TimeOfDay latest = TimeOfDay::MaxValue;
                if (midnight.Ticks != {{Literal(TimeOnly.MinValue.Ticks)}} || latest.Ticks != {{Literal(TimeOnly.MaxValue.Ticks)}}) return false;
                if (latest.Hour != 23 || latest.Minute != 59 || latest.Second != 59 || latest.Millisecond != 999 || latest.Microsecond != 999 || latest.Nanosecond != 900) return false;
                DateTime start = DateTime::MinValue;
                DateTime end = DateTime::MaxValue;
                if (start.Ticks != {{Literal(DateTime.MinValue.Ticks)}} || end.Ticks != {{Literal(DateTime.MaxValue.Ticks)}}) return false;
                if (end.Date.DayNumber != last.DayNumber || end.TimeOfDay.Ticks != latest.Ticks) return false;
                DateTimeOffset earliest = DateTimeOffset::MinValue;
                DateTimeOffset final = DateTimeOffset::MaxValue;
                if (earliest.UtcTicks != {{Literal(DateTimeOffset.MinValue.UtcTicks)}} || final.UtcTicks != {{Literal(DateTimeOffset.MaxValue.UtcTicks)}}) return false;
                if (earliest.Offset.Ticks != 0l || final.Offset.Ticks != 0l || final.Ticks != end.Ticks) return false;
                return true;
            }
            int main()
            {
                int before = attempts;
                deny = true;
                for (int i = 0; i < 10; i++) if (!Observe()) return 1;
                TimeSpan duration = TimeSpan::MaxValue;
                duration = TimeSpan::Zero;
                Date date = Date::MaxValue;
                date = Date::MinValue;
                TimeOfDay time = TimeOfDay::MaxValue;
                time = TimeOfDay::MinValue;
                DateTime clock = DateTime::MaxValue;
                clock = DateTime::MinValue;
                DateTimeOffset offset = DateTimeOffset::MaxValue;
                offset = DateTimeOffset::MinValue;
                if (!Observe() || attempts != before || duration.Ticks != 0l || date.DayNumber != 0 || time.Ticks != 0l || clock.Ticks != 0l || offset.UtcTicks != 0l) return 2;
                deny = false;
                return 0;
            }
            """;
        Run(program, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EndpointArithmeticWrapsOrThrowsAccordingToTheExistingTypeContracts(int optimization)
        => Run("""
            using Standard;
            using Standard::Time;

            int main()
            {
                int caught = 0;
                Date date = Date::MinValue;
                try { date = date.AddDays(-1); }
                catch (Exception* error) { caught++; }
                if (date.DayNumber != 0) return 1;
                date = Date::MaxValue;
                try { date = date.AddDays(1); }
                catch (Exception* error) { caught++; }
                if (date.Year != 9999) return 2;
                DateTime clock = DateTime::MinValue;
                try { clock = clock.AddTicks(-1l); }
                catch (Exception* error) { caught++; }
                if (clock.Ticks != 0l) return 3;
                clock = DateTime::MaxValue;
                long maximum = clock.Ticks;
                try { clock = clock.AddTicks(1l); }
                catch (Exception* error) { caught++; }
                if (clock.Ticks != maximum) return 4;
                TimeSpan duration = TimeSpan::MinValue;
                try { duration = duration.Negate(); }
                catch (Exception* error) { caught++; }
                if (duration.Ticks != -9223372036854775807l - 1l) return 5;
                duration = TimeSpan::MaxValue;
                try { duration = duration.Add(TimeSpan::FromTicks(1l)); }
                catch (Exception* error) { caught++; }
                if (duration.Ticks != 9223372036854775807l) return 6;
                TimeOfDay time = TimeOfDay::MaxValue;
                int days = 0;
                time = time.AddTicks(1l, ref days);
                if (time.Ticks != 0l || days != 1) return 7;
                time = TimeOfDay::MinValue;
                time = time.AddTicks(-1l, ref days);
                if (time.Ticks != 863999999999l || days != -1) return 8;
                DateTimeOffset offset = DateTimeOffset::MinValue;
                try { offset = offset.ToOffset(TimeSpan::FromHours(-1l)); }
                catch (Exception* error) { caught++; }
                if (offset.UtcTicks != 0l || offset.Offset.Ticks != 0l) return 9;
                offset = DateTimeOffset::MaxValue;
                try { offset = offset.ToOffset(TimeSpan::FromHours(1l)); }
                catch (Exception* error) { caught++; }
                if (offset.UtcTicks != maximum || offset.Offset.Ticks != 0l || caught != 8) return 10;
                DateTimeOffset same = offset.ToOffset(TimeSpan::FromHours(-1l));
                if (!same.Equals(&offset) || same.UtcTicks != maximum) return 11;
                return 0;
            }
            """, optimization);

    private static string Literal(long value) => value == long.MinValue ? "-9223372036854775807l - 1l" : value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "l";

    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Time.Append(new SourceFile("temporal-limits.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"Temporal limits exit {result.ExitCode}: {result.StandardError}");
    }
}
