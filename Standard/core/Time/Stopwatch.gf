namespace Standard::Time;

// Ordinary value state; C is selected by the caller, with no global clock hook.
public struct Stopwatch<C : IMonotonicClock>
{
    private static const long MaximumTicks = 9223372036854775807l;
    private static const long DurationTicksPerSecond = 10000000l;
    private long start;
    private long accumulated;
    private long frequency;
    private bool running;

    public Stopwatch()
    {
        start = 0l; accumulated = 0l; frequency = 0l; running = false;
        long selected = C::GetFrequency();
        if (selected <= 0l) throw new Exception("Clock frequency must be positive");
        frequency = selected;
    }

    public static Stopwatch<C> StartNew()
    {
        Stopwatch<C> result = new();
        result.Start();
        return result;
    }
    public bool IsRunning { immutable get nothrow => running; }

    private immutable long ReadTotal()
    {
        if (!running) return accumulated;
        long end = C::GetTimestamp();
        if (end < start) throw new Exception("Clock timestamp moved backwards");
        if (start < 0l && end > MaximumTicks + start)
            throw new Exception("Stopwatch elapsed counter overflow");
        long delta = end - start;
        if (delta > MaximumTicks - accumulated)
            throw new Exception("Stopwatch elapsed counter overflow");
        return accumulated + delta;
    }

    public void Start()
    {
        if (running) return;
        long now = C::GetTimestamp();
        start = now; running = true;
    }
    public void Stop()
    {
        if (!running) return;
        long total = this.ReadTotal();
        accumulated = total; running = false;
    }
    public void Reset() nothrow { start = 0l; accumulated = 0l; running = false; }
    public void Restart()
    {
        long now = C::GetTimestamp();
        start = now; accumulated = 0l; running = true;
    }

    // Raw provider counter units, not the 100 ns units used by TimeSpan.
    public long ElapsedTicks { immutable get => this.ReadTotal(); }
    public double ElapsedSeconds { immutable get => (double)this.ReadTotal() / (double)frequency; }

    // floor(part * 10^7 / divisor), where part < divisor <= long maximum.
    // Reduce after doubling AND after addition, so neither step reaches 2^64.
    private static ulong ScaleFraction(ulong part, ulong divisor) nothrow
    {
        ulong quotient = 0ul;
        ulong remainder = 0ul;
        for (uint bit = 8388608u; bit != 0u; bit = bit >> 1)
        {
            quotient *= 2ul; remainder *= 2ul;
            if (remainder >= divisor) { remainder -= divisor; quotient++; }
            if ((10000000u & bit) != 0u)
            {
                remainder += part;
                if (remainder >= divisor) { remainder -= divisor; quotient++; }
            }
        }
        return quotient;
    }

    public TimeSpan Elapsed
    {
        immutable get
        {
            long total = this.ReadTotal();
            long whole = total / frequency;
            if (whole > MaximumTicks / DurationTicksPerSecond)
                throw new Exception("Stopwatch duration overflow");
            long ticks = whole * DurationTicksPerSecond;
            long fraction = (long)ScaleFraction((ulong)(total % frequency), (ulong)frequency);
            if (fraction > MaximumTicks - ticks) throw new Exception("Stopwatch duration overflow");
            return TimeSpan::FromTicks(ticks + fraction);
        }
    }
}
