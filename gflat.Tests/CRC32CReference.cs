using System.Numerics;

namespace gflat.Tests;

// Deliberately uses forward polynomial long division, not the native reflected
// shift/XOR recurrence. Blocks bound BigInteger width even for large CLI inputs.
internal static class CRC32CReference
{
    internal static uint Compute(ReadOnlySpan<byte> bytes)
    {
        BigInteger remainder = uint.MaxValue;
        BigInteger generator = 0x11edc6f41ul;
        while (!bytes.IsEmpty)
        {
            int count = Math.Min(bytes.Length, 128);
            BigInteger message = BigInteger.Zero;
            foreach (byte value in bytes[..count])
            {
                int reversed = 0;
                for (int bit = 0; bit < 8; bit++) reversed = (reversed << 1) | ((value >> bit) & 1);
                message = (message << 8) | reversed;
            }
            remainder = (remainder << (count * 8)) ^ (message << 32);
            for (int degree = checked((int)remainder.GetBitLength()) - 1; degree >= 32; degree--)
                if ((remainder & (BigInteger.One << degree)) != 0) remainder ^= generator << (degree - 32);
            bytes = bytes[count..];
        }
        uint forward = (uint)remainder;
        uint reflected = 0;
        for (int bit = 0; bit < 32; bit++) reflected = (reflected << 1) | ((forward >> bit) & 1u);
        return reflected ^ uint.MaxValue;
    }
}
