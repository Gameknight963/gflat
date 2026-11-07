using Standard::Numerics;

namespace Standard::Time;

namespace time_span_detail
{
    private TimeSpan FromRoundedTicks(double ticks)
    {
        double limit = 9223372036854775808d;
        if (ticks != ticks || ticks < -limit || ticks > limit)
            throw new Exception("TimeSpan scaling overflow");
        if (ticks == limit) return TimeSpan::FromTicks(9223372036854775807l);
        return TimeSpan::FromTicks((long)ticks);
    }
}

public TimeSpan Multiply(TimeSpan value, double factor)
    => value.Multiply(factor);

public TimeSpan Divide(TimeSpan value, double divisor)
    => value.Divide(divisor);

public double Divide(TimeSpan value, TimeSpan divisor) nothrow
    => value.Divide(divisor);

extension TimeSpan
{
    public immutable TimeSpan Multiply(double factor)
    {
        if (factor != factor) throw new Exception("TimeSpan factor must not be NaN");
        double scaled = (double)(*this).Ticks * factor;
        return time_span_detail::FromRoundedTicks(Math::Round(scaled));
    }

    public immutable TimeSpan Divide(double divisor)
    {
        if (divisor != divisor) throw new Exception("TimeSpan divisor must not be NaN");
        double scaled = (double)(*this).Ticks / divisor;
        return time_span_detail::FromRoundedTicks(Math::Round(scaled));
    }

    public immutable double Divide(TimeSpan divisor) nothrow
        => (double)(*this).Ticks / (double)divisor.Ticks;
}
