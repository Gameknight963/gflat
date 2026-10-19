namespace std;

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
        for (nuint i = 0; i < count; i++)
        {
            first += (uint)(byte)data[i];
            if (first >= 65521u) first -= 65521u;
            second += first;
            if (second >= 65521u) second -= 65521u;
        }
    }

    public readonly uint GetValue() nothrow => (second << 16) | first;
}
