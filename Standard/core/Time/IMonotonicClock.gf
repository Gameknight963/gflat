namespace Standard::Time;

// Timestamp units are provider-defined. Frequency is positive and fixed.
// Samples must be monotonic; methods may throw on provider failure.
public static interface IMonotonicClock
{
    long GetTimestamp();
    long GetFrequency();
}
