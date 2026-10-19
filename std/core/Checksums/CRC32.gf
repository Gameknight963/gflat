namespace std;

// Streaming IEEE CRC-32, as used by gzip (RFC 1952).
public struct CRC32
{
    private uint state;

    public CRC32() nothrow { state = 0xffffffffu; }
    public void Reset() nothrow { state = 0xffffffffu; }

    // Counted bytes, including embedded zeros. The input is borrowed for this call.
    public void Append(readonly(char)* data, nuint count) nothrow
    {
        for (nuint i = 0; i < count; i++)
        {
            state = state ^ (uint)(byte)data[i];
            for (int bit = 0; bit < 8; bit++)
            {
                if ((state & 1u) != 0u) state = (state >> 1) ^ 0xedb88320u;
                else state = state >> 1;
            }
        }
    }

    // Returns the finalized numeric checksum without changing the streaming state.
    public readonly uint GetValue() nothrow => state ^ 0xffffffffu;
}
