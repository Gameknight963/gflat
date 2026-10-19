namespace std;

// Unaligned, explicitly ordered integers. Counts describe readable/writable bytes.
public static class BinaryPrimitives
{
    public static bool TryReadUInt16LittleEndian(readonly(byte)* data, nuint count, ref ushort value) nothrow
    {
        if (count < 2) { value = 0; return false; }
        value = (ushort)Read(data, 2, true); return true;
    }
    public static bool TryReadUInt16BigEndian(readonly(byte)* data, nuint count, ref ushort value) nothrow
    {
        if (count < 2) { value = 0; return false; }
        value = (ushort)Read(data, 2, false); return true;
    }
    public static bool TryReadUInt32LittleEndian(readonly(byte)* data, nuint count, ref uint value) nothrow
    {
        if (count < 4) { value = 0; return false; }
        value = (uint)Read(data, 4, true); return true;
    }
    public static bool TryReadUInt32BigEndian(readonly(byte)* data, nuint count, ref uint value) nothrow
    {
        if (count < 4) { value = 0; return false; }
        value = (uint)Read(data, 4, false); return true;
    }
    public static bool TryReadUInt64LittleEndian(readonly(byte)* data, nuint count, ref ulong value) nothrow
    {
        if (count < 8) { value = 0; return false; }
        value = Read(data, 8, true); return true;
    }
    public static bool TryReadUInt64BigEndian(readonly(byte)* data, nuint count, ref ulong value) nothrow
    {
        if (count < 8) { value = 0; return false; }
        value = Read(data, 8, false); return true;
    }

    public static bool TryWriteUInt16LittleEndian(byte* data, nuint count, ushort value) nothrow
        => Write(data, count, (ulong)value, 2, true);
    public static bool TryWriteUInt16BigEndian(byte* data, nuint count, ushort value) nothrow
        => Write(data, count, (ulong)value, 2, false);
    public static bool TryWriteUInt32LittleEndian(byte* data, nuint count, uint value) nothrow
        => Write(data, count, (ulong)value, 4, true);
    public static bool TryWriteUInt32BigEndian(byte* data, nuint count, uint value) nothrow
        => Write(data, count, (ulong)value, 4, false);
    public static bool TryWriteUInt64LittleEndian(byte* data, nuint count, ulong value) nothrow
        => Write(data, count, value, 8, true);
    public static bool TryWriteUInt64BigEndian(byte* data, nuint count, ulong value) nothrow
        => Write(data, count, value, 8, false);

    private static ulong Read(readonly(byte)* data, nuint width, bool littleEndian) nothrow
    {
        ulong value = 0ul;
        for (nuint i = 0; i < width; i++)
        {
            nuint index = i;
            if (littleEndian) index = width - 1 - i;
            value = (value << 8) | (ulong)data[index];
        }
        return value;
    }

    private static bool Write(byte* data, nuint count, ulong value, nuint width, bool littleEndian) nothrow
    {
        if (count < width) return false;
        for (nuint i = 0; i < width; i++)
        {
            nuint index = i;
            if (!littleEndian) index = width - 1 - i;
            data[index] = (byte)value;
            value = value >> 8;
        }
        return true;
    }
}
