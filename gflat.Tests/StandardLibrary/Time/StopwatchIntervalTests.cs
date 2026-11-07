using System.Numerics;

namespace gflat.Tests.Std.Time;

public sealed class StopwatchIntervalTests
{
    private const string Support = """
        using Standard;
        using Standard::Time;

        long now = 0l;
        long frequency = 3l;
        int timestamps = 0;
        int frequencies = 0;
        bool failTimestamp = false;
        bool failFrequency = false;
        static class Clock : IMonotonicClock
        {
            public static long GetTimestamp()
            {
                timestamps++;
                if (failTimestamp) throw new Exception("timestamp", 91);
                return now;
            }
            public static long GetFrequency()
            {
                frequencies++;
                if (failFrequency) throw new Exception("frequency", 92);
                return frequency;
            }
        }
        """;

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("STOPWATCH_INTERVALS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SuccessfulIntervalHelpersAndMillisecondsAllocateNoStorage(int optimization)
        => Run(Support + """
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
            int main()
            {
                int before = attempts;
                deny = true;
                frequency = 3l;
                now = 10l;
                if (Stopwatch<Clock>::Frequency != 3l || Stopwatch<Clock>::GetTimestamp() != 10l) return 1;
                TimeSpan forward = Stopwatch<Clock>::GetElapsedTime(1l, 5l);
                TimeSpan reverse = Stopwatch<Clock>::GetElapsedTime(5l, 1l);
                if (forward.Ticks != 13333333l || reverse.Ticks != -13333333l) return 2;
                Stopwatch<Clock> watch = new();
                watch.Start();
                now = 14l;
                if (watch.ElapsedMilliseconds != 1333l) return 3;
                TimeSpan duration = watch.Elapsed;
                if (duration.Ticks != 13333333l) return 4;
                frequency = 10000000l;
                TimeSpan minimum = Stopwatch<Clock>::GetElapsedTime(0l, -9223372036854775807l - 1l);
                if (minimum.Ticks != -9223372036854775807l - 1l) return 5;
                frequency = 9223372036854775807l;
                TimeSpan full = Stopwatch<Clock>::GetElapsedTime(-9223372036854775807l - 1l, 9223372036854775807l);
                if (full.Ticks != 20000000l || attempts != before) return 6;
                deny = false;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SignedIntervalsMatchBigIntegerAcrossCounterAndTimeSpanBoundaries(int optimization)
    {
        var rows = new List<(long Frequency, long Start, long End)>();
        long[] frequencies = [1, 2, 3, 7, 9999999, 10000000, 10000001, 1000000000, long.MaxValue];
        long[] points = [long.MinValue, long.MinValue + 1, -1, 0, 1, long.MaxValue - 1, long.MaxValue];
        foreach (long frequency in frequencies)
        {
            foreach (long start in points)
            foreach (long end in points) rows.Add((frequency, start, end));
            BigInteger boundary = (BigInteger)long.MaxValue * frequency / 10000000;
            if (boundary < long.MaxValue)
            {
                for (int adjustment = -1; adjustment <= 1; adjustment++)
                {
                    long distance = (long)boundary + adjustment;
                    rows.Add((frequency, 0, distance));
                    rows.Add((frequency, 0, -distance));
                }
            }
        }
        var random = new Random(517307);
        for (int i = 0; i < 128; i++)
            rows.Add((frequencies[i % frequencies.Length], random.NextInt64(long.MinValue, long.MaxValue), random.NextInt64(long.MinValue, long.MaxValue)));
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true))
        foreach (var row in rows)
        {
            BigInteger exact = ((BigInteger)row.End - row.Start) * 10000000 / row.Frequency;
            bool fits = exact >= long.MinValue && exact <= long.MaxValue;
            writer.Write(row.Frequency);
            writer.Write(row.Start);
            writer.Write(row.End);
            writer.Write(fits ? 1L : 0L);
            writer.Write(fits ? (long)exact : 0L);
        }
        string hex = Convert.ToHexString(bytes.ToArray());
        Run(Support + $$"""
            int Nibble(char value)
            {
                if (value <= '9') return (int)value - (int)'0';
                return (int)value - (int)'A' + 10;
            }
            long Read(readonly(char)* input, ref nuint position)
            {
                ulong value = 0ul;
                for (int shift = 0; shift < 64; shift += 8)
                {
                    int octet = Nibble(input[position]) * 16 + Nibble(input[position + 1]);
                    position += 2;
                    value = value | ((ulong)octet << shift);
                }
                return (long)value;
            }
            int main()
            {
                readonly(char)* data = "{{hex}}";
                nuint position = 0;
                for (int row = 0; row < {{rows.Count}}; row++)
                {
                    frequency = Read(data, ref position);
                    long start = Read(data, ref position);
                    long end = Read(data, ref position);
                    bool fits = Read(data, ref position) != 0l;
                    long expected = Read(data, ref position);
                    int beforeTimestamp = timestamps;
                    int beforeFrequency = frequencies;
                    TimeSpan output = TimeSpan::FromTicks(123l);
                    bool caught = false;
                    try { output = Stopwatch<Clock>::GetElapsedTime(start, end); }
                    catch (Exception* error) { caught = true; }
                    if (caught == fits || timestamps != beforeTimestamp || frequencies != beforeFrequency + 1) return 1;
                    if (fits) { if (output.Ticks != expected) return 2; }
                    else if (output.Ticks != 123l) return 3;
                }
                if (position != {{hex.Length}}) return 4;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HelpersSampleOnlyRequiredProviderMembersAndMillisecondsUseCachedFrequency(int optimization)
        => Run(Support + """
            int main()
            {
                now = -10l;
                if (Stopwatch<Clock>::Frequency != 3l || frequencies != 1 || timestamps != 0) return 1;
                long start = Stopwatch<Clock>::GetTimestamp();
                if (start != -10l || timestamps != 1 || frequencies != 1) return 2;
                now = -6l;
                TimeSpan elapsed = Stopwatch<Clock>::GetElapsedTime(start);
                if (elapsed.Ticks != 13333333l || timestamps != 2 || frequencies != 2) return 3;
                Stopwatch<Clock> watch = new();
                watch.Start();
                now = -2l;
                int beforeTimestamp = timestamps;
                int beforeFrequency = frequencies;
                if (watch.ElapsedMilliseconds != 1333l || timestamps != beforeTimestamp + 1 || frequencies != beforeFrequency) return 4;
                watch.Stop();
                beforeTimestamp = timestamps;
                frequency = 0l;
                if (watch.ElapsedMilliseconds != 1333l || timestamps != beforeTimestamp || frequencies != beforeFrequency) return 5;
                bool caught = false;
                try { Stopwatch<Clock>::GetElapsedTime(0l, 0l); }
                catch (Exception* error) { caught = true; }
                if (!caught || timestamps != beforeTimestamp) return 6;
                failFrequency = true;
                caught = false;
                try { Stopwatch<Clock>::Frequency; }
                catch (ref Exception error) { if (error.GetCode() != 92) return 7; caught = true; }
                if (!caught) return 8;
                failFrequency = false;
                failTimestamp = true;
                beforeFrequency = frequencies;
                caught = false;
                try { Stopwatch<Clock>::GetElapsedTime(0l); }
                catch (ref Exception error) { if (error.GetCode() != 91) return 9; caught = true; }
                if (!caught || frequencies != beforeFrequency) return 10;
                failTimestamp = false;
                frequency = 1l;
                now = 0l;
                watch = new Stopwatch<Clock>();
                watch.Start();
                now = 2000000000000l;
                if (watch.ElapsedMilliseconds != 2000000000000000l) return 11;
                caught = false;
                try { watch.Elapsed; }
                catch (Exception* error) { caught = true; }
                if (!caught || watch.ElapsedTicks != now) return 12;
                now = 9223372036854775807l;
                caught = false;
                try { watch.ElapsedMilliseconds; }
                catch (Exception* error) { caught = true; }
                if (!caught || !watch.IsRunning || watch.ElapsedTicks != now) return 13;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Time.Append(new SourceFile("stopwatch-interval.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"Stopwatch interval exit {result.ExitCode}: {result.StandardError}");
    }
}
