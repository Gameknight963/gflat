namespace Standard::Time;

namespace temporal_arithmetic_detail
{
    private TimeSpan UnitDelta(double value, long scale)
    {
        double maximum = (double)(3155378975999999999l / scale);
        if (value != value || value < -maximum || value > maximum)
            throw new Exception("Date/time unit addition is out of range");
        long whole = (long)value;
        double fraction = value - (double)whole;
        return TimeSpan::FromTicks(whole * scale + (long)(fraction * (double)scale));
    }
}

public DateTime AddDays(DateTime value, long amount)
    => value.AddDays(amount);

public DateTime AddDays(DateTime value, double amount)
    => value.AddDays(amount);

public DateTime AddHours(DateTime value, long amount)
    => value.AddHours(amount);

public DateTime AddHours(DateTime value, double amount)
    => value.AddHours(amount);

public DateTime AddMinutes(DateTime value, long amount)
    => value.AddMinutes(amount);

public DateTime AddMinutes(DateTime value, double amount)
    => value.AddMinutes(amount);

public DateTime AddSeconds(DateTime value, long amount)
    => value.AddSeconds(amount);

public DateTime AddSeconds(DateTime value, double amount)
    => value.AddSeconds(amount);

public DateTime AddMilliseconds(DateTime value, long amount)
    => value.AddMilliseconds(amount);

public DateTime AddMilliseconds(DateTime value, double amount)
    => value.AddMilliseconds(amount);

public DateTime AddMicroseconds(DateTime value, long amount)
    => value.AddMicroseconds(amount);

public DateTime AddMicroseconds(DateTime value, double amount)
    => value.AddMicroseconds(amount);

extension DateTime
{
    public immutable DateTime AddDays(long amount)
        => (*this).Add(TimeSpan::FromDays(amount));

    public immutable DateTime AddDays(double amount)
        => (*this).Add(temporal_arithmetic_detail::UnitDelta(amount, 864000000000l));

    public immutable DateTime AddHours(long amount)
        => (*this).Add(TimeSpan::FromHours(amount));

    public immutable DateTime AddHours(double amount)
        => (*this).Add(temporal_arithmetic_detail::UnitDelta(amount, 36000000000l));

    public immutable DateTime AddMinutes(long amount)
        => (*this).Add(TimeSpan::FromMinutes(amount));

    public immutable DateTime AddMinutes(double amount)
        => (*this).Add(temporal_arithmetic_detail::UnitDelta(amount, 600000000l));

    public immutable DateTime AddSeconds(long amount)
        => (*this).Add(TimeSpan::FromSeconds(amount));

    public immutable DateTime AddSeconds(double amount)
        => (*this).Add(temporal_arithmetic_detail::UnitDelta(amount, 10000000l));

    public immutable DateTime AddMilliseconds(long amount)
        => (*this).Add(TimeSpan::FromMilliseconds(amount));

    public immutable DateTime AddMilliseconds(double amount)
        => (*this).Add(temporal_arithmetic_detail::UnitDelta(amount, 10000l));

    public immutable DateTime AddMicroseconds(long amount)
        => (*this).Add(TimeSpan::FromMicroseconds(amount));

    public immutable DateTime AddMicroseconds(double amount)
        => (*this).Add(temporal_arithmetic_detail::UnitDelta(amount, 10l));
}

public DateTimeOffset AddDays(DateTimeOffset value, long amount)
    => value.AddDays(amount);

public DateTimeOffset AddDays(DateTimeOffset value, double amount)
    => value.AddDays(amount);

public DateTimeOffset AddHours(DateTimeOffset value, long amount)
    => value.AddHours(amount);

public DateTimeOffset AddHours(DateTimeOffset value, double amount)
    => value.AddHours(amount);

public DateTimeOffset AddMinutes(DateTimeOffset value, long amount)
    => value.AddMinutes(amount);

public DateTimeOffset AddMinutes(DateTimeOffset value, double amount)
    => value.AddMinutes(amount);

public DateTimeOffset AddSeconds(DateTimeOffset value, long amount)
    => value.AddSeconds(amount);

public DateTimeOffset AddSeconds(DateTimeOffset value, double amount)
    => value.AddSeconds(amount);

public DateTimeOffset AddMilliseconds(DateTimeOffset value, long amount)
    => value.AddMilliseconds(amount);

public DateTimeOffset AddMilliseconds(DateTimeOffset value, double amount)
    => value.AddMilliseconds(amount);

public DateTimeOffset AddMicroseconds(DateTimeOffset value, long amount)
    => value.AddMicroseconds(amount);

public DateTimeOffset AddMicroseconds(DateTimeOffset value, double amount)
    => value.AddMicroseconds(amount);

extension DateTimeOffset
{
    public immutable DateTimeOffset AddDays(long amount)
        => (*this).Add(TimeSpan::FromDays(amount));

    public immutable DateTimeOffset AddDays(double amount)
        => (*this).Add(temporal_arithmetic_detail::UnitDelta(amount, 864000000000l));

    public immutable DateTimeOffset AddHours(long amount)
        => (*this).Add(TimeSpan::FromHours(amount));

    public immutable DateTimeOffset AddHours(double amount)
        => (*this).Add(temporal_arithmetic_detail::UnitDelta(amount, 36000000000l));

    public immutable DateTimeOffset AddMinutes(long amount)
        => (*this).Add(TimeSpan::FromMinutes(amount));

    public immutable DateTimeOffset AddMinutes(double amount)
        => (*this).Add(temporal_arithmetic_detail::UnitDelta(amount, 600000000l));

    public immutable DateTimeOffset AddSeconds(long amount)
        => (*this).Add(TimeSpan::FromSeconds(amount));

    public immutable DateTimeOffset AddSeconds(double amount)
        => (*this).Add(temporal_arithmetic_detail::UnitDelta(amount, 10000000l));

    public immutable DateTimeOffset AddMilliseconds(long amount)
        => (*this).Add(TimeSpan::FromMilliseconds(amount));

    public immutable DateTimeOffset AddMilliseconds(double amount)
        => (*this).Add(temporal_arithmetic_detail::UnitDelta(amount, 10000l));

    public immutable DateTimeOffset AddMicroseconds(long amount)
        => (*this).Add(TimeSpan::FromMicroseconds(amount));

    public immutable DateTimeOffset AddMicroseconds(double amount)
        => (*this).Add(temporal_arithmetic_detail::UnitDelta(amount, 10l));
}

