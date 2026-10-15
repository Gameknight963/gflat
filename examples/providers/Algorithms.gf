namespace Algorithms;

// These small algorithms illustrate configuration, not cryptographic hashing.
public static class Sum : Hashing::IByteHash
{
    public static uint Seed => 0;
    public static uint Append(uint state, byte value) => state + (uint)value;
}

public static class Xor : Hashing::IByteHash
{
    public static uint Seed => 0;
    public static uint Append(uint state, byte value) => state ^ (uint)value;
}
