namespace Standard::Numerics;

// SplitMix64, deterministic and explicitly seeded. Not a cryptographic generator.
// Algorithm: Sebastiano Vigna's public-domain reference, https://prng.di.unimi.it/splitmix64.c
struct PseudoRandom
{
    private ulong state;

    public PseudoRandom(ulong seed) nothrow { state = seed; }

    public ulong NextULong() nothrow
    {
        state += 0x9e3779b97f4a7c15ul;
        ulong value = state;
        value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9ul;
        value = (value ^ (value >> 27)) * 0x94d049bb133111ebul;
        return value ^ (value >> 31);
    }

    public uint NextUInt() nothrow => (uint)(this.NextULong() >> 32);

    // All 32 signed bits, including negative values; consumes one complete word.
    public int NextInt() nothrow => (int)this.NextUInt();

    // Rejection removes the modulo bias when the range does not divide 2^64.
    public ulong NextULong(ulong exclusiveMax)
    {
        if (exclusiveMax == 0ul) throw new Exception("Random upper bound must be positive");
        ulong threshold = (0ul - exclusiveMax) % exclusiveMax;
        ulong value = this.NextULong();
        while (value < threshold) value = this.NextULong();
        return value % exclusiveMax;
    }

    public uint NextUInt(uint exclusiveMax)
    {
        if (exclusiveMax == 0u) throw new Exception("Random upper bound must be positive");
        return (uint)this.NextULong((ulong)exclusiveMax);
    }

    public int NextInt(int exclusiveMax) => this.NextInt(0, exclusiveMax);

    // The interval includes inclusiveMin and excludes exclusiveMax.
    public int NextInt(int inclusiveMin, int exclusiveMax)
    {
        if (inclusiveMin >= exclusiveMax) throw new Exception("Random range must not be empty");
        ulong width = (ulong)((long)exclusiveMax - (long)inclusiveMin);
        return (int)((long)inclusiveMin + (long)this.NextULong(width));
    }

    // All 64 signed bits; consumes exactly one complete generated word.
    public long NextLong() nothrow => (long)this.NextULong();

    public long NextLong(long exclusiveMax) => this.NextLong(0l, exclusiveMax);

    // Unsigned subtraction/addition also covers widths greater than long.MaxValue.
    public long NextLong(long inclusiveMin, long exclusiveMax)
    {
        if (inclusiveMin >= exclusiveMax) throw new Exception("Random range must not be empty");
        ulong width = (ulong)exclusiveMax - (ulong)inclusiveMin;
        ulong offset = this.NextULong(width);
        return (long)((ulong)inclusiveMin + offset);
    }

    // 24 random bits, producing a finite value in [0, 1).
    // Convert the selected bits directly; rounding NextDouble to float can yield 1.
    public float NextFloat() nothrow
        => (float)(this.NextULong() >> 40) * (1.0f / 16777216.0f);

    // 53 random bits, producing a finite value in [0, 1).
    public double NextDouble() nothrow
        => (double)(this.NextULong() >> 11) * (1.0d / 9007199254740992.0d);

    // Writes little-endian bytes from each generated word. No allocation or terminator.
    // A partial final word is consumed in full; separate Fill calls need not concatenate.
    public void Fill(char* output, nuint count) nothrow
    {
        nuint index = 0;
        ulong value = 0ul;
        while (index < count)
        {
            value = this.NextULong();
            for (int shift = 0; shift < 64 && index < count; shift += 8)
            {
                output[index] = (char)(value >> shift);
                index++;
            }
        }
    }
}
