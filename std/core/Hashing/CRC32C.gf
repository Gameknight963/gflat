namespace std;

// Streaming CRC-32C (Castagnoli), with the reflected representation of RFC 3720.
public struct CRC32C
{
    private uint state;

    public CRC32C() nothrow { state = 0xffffffffu; }
    public void Reset() nothrow { state = 0xffffffffu; }

    // Borrows counted bytes, including zeros. Stage state so input may overlap it.
    public void Append(readonly(char)* data, nuint count) nothrow
    {
        uint current = state;
        for (nuint i = 0; i < count; i++)
        {
            current = current ^ (uint)(byte)data[i];
            for (int bit = 0; bit < 8; bit++)
            {
                if ((current & 1u) != 0u) current = (current >> 1) ^ 0x82f63b78u;
                else current = current >> 1;
            }
        }
        state = current;
    }

    // Numeric result; serialization byte order is a separate choice.
    public immutable uint GetValue() nothrow => state ^ 0xffffffffu;
}
