namespace gflat.Tests.Std.Time;

public sealed class MonotonicClockTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("MONOTONIC_CLOCK.md", 0);

    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Time/TimeSpan.gf", "core/Time/IMonotonicClock.gf",
            "core/Time/Stopwatch.gf", "libc/Time/MonotonicClock.gf"))
        .Append(new SourceFile("monotonic-clock.gf", code));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SystemSamplesAreMonotonicAndStopwatchUsesThemWithoutAllocating(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Time;

            int requests = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { requests++; return null; }
                public replace void Free(void* storage) nothrow { }
            }
            int main()
            {
                requests = 0;
                long frequency = MonotonicClock::GetFrequency();
                if (frequency <= 0l) return 1;
                long start = MonotonicClock::GetTimestamp();
                long previous = start;
                var timer = Stopwatch<MonotonicClock>::StartNew();
                if (!timer.IsRunning || Stopwatch<MonotonicClock>::Frequency != frequency) return 2;
                for (int i = 0; i < 10000; i++)
                {
                    long current = MonotonicClock::GetTimestamp();
                    if (current < previous) return 3;
                    previous = current;
                }
                timer.Stop();
                if (previous <= start || timer.IsRunning || timer.ElapsedTicks <= 0l || timer.Elapsed.Ticks <= 0l) return 4;
                long stopped = timer.ElapsedTicks;
                long end = Stopwatch<MonotonicClock>::GetTimestamp();
                TimeSpan interval = Stopwatch<MonotonicClock>::GetElapsedTime(start, end);
                if (interval.Ticks <= 0l || timer.ElapsedTicks != stopped) return 5;
                timer.Reset();
                if (timer.IsRunning || timer.ElapsedTicks != 0l) return 6;
                timer.Restart();
                timer.Stop();
                if (timer.ElapsedTicks < 0l || requests != 0) return 7;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
