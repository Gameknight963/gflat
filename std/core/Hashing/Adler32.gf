namespace std::Hashing;

// Streaming Adler-32, as used by zlib (RFC 1950).
public struct Adler32
{
    private uint first;
    private uint second;

    public Adler32() nothrow { first = 1u; second = 0u; }
    public void Reset() nothrow { first = 1u; second = 0u; }

    // Each accumulator stays below 65521 after every byte. A single subtraction
    // is sufficient, avoiding overflow even for arbitrarily long streams.
    public void Append(readonly(char)* data, nuint count) nothrow
    {
        uint runningFirst = first;
        uint runningSecond = second;
        for (nuint i = 0; i < count; i++)
        {
            runningFirst += (uint)(byte)data[i];
            if (runningFirst >= 65521u) runningFirst -= 65521u;
            runningSecond += runningFirst;
            if (runningSecond >= 65521u) runningSecond -= 65521u;
        }
        first = runningFirst;
        second = runningSecond;
    }

    public immutable uint GetValue() nothrow => (second << 16) | first;
}
