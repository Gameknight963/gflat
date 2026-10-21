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

    public static bool TryReadInt16LittleEndian(readonly(byte)* data, nuint count, ref short value) nothrow
    {
        ushort bits = (ushort)0;
        bool success = TryReadUInt16LittleEndian(data, count, ref bits);
        value = (short)bits; return success;
    }
    public static bool TryWriteInt16LittleEndian(byte* data, nuint count, short value) nothrow
        => TryWriteUInt16LittleEndian(data, count, (ushort)value);

    public static bool TryReadInt16BigEndian(readonly(byte)* data, nuint count, ref short value) nothrow
    {
        ushort bits = (ushort)0;
        bool success = TryReadUInt16BigEndian(data, count, ref bits);
        value = (short)bits; return success;
    }
    public static bool TryWriteInt16BigEndian(byte* data, nuint count, short value) nothrow
        => TryWriteUInt16BigEndian(data, count, (ushort)value);

    public static bool TryReadInt32LittleEndian(readonly(byte)* data, nuint count, ref int value) nothrow
    {
        uint bits = (uint)0;
        bool success = TryReadUInt32LittleEndian(data, count, ref bits);
        value = (int)bits; return success;
    }
    public static bool TryWriteInt32LittleEndian(byte* data, nuint count, int value) nothrow
        => TryWriteUInt32LittleEndian(data, count, (uint)value);

    public static bool TryReadInt32BigEndian(readonly(byte)* data, nuint count, ref int value) nothrow
    {
        uint bits = (uint)0;
        bool success = TryReadUInt32BigEndian(data, count, ref bits);
        value = (int)bits; return success;
    }
    public static bool TryWriteInt32BigEndian(byte* data, nuint count, int value) nothrow
        => TryWriteUInt32BigEndian(data, count, (uint)value);

    public static bool TryReadInt64LittleEndian(readonly(byte)* data, nuint count, ref long value) nothrow
    {
        ulong bits = (ulong)0;
        bool success = TryReadUInt64LittleEndian(data, count, ref bits);
        value = (long)bits; return success;
    }
    public static bool TryWriteInt64LittleEndian(byte* data, nuint count, long value) nothrow
        => TryWriteUInt64LittleEndian(data, count, (ulong)value);

    public static bool TryReadInt64BigEndian(readonly(byte)* data, nuint count, ref long value) nothrow
    {
        ulong bits = (ulong)0;
        bool success = TryReadUInt64BigEndian(data, count, ref bits);
        value = (long)bits; return success;
    }
    public static bool TryWriteInt64BigEndian(byte* data, nuint count, long value) nothrow
        => TryWriteUInt64BigEndian(data, count, (ulong)value);

    // Decode byte order first; aligned locals preserve IEEE bits without a dependency.
    public static bool TryReadFloatLittleEndian(readonly(byte)* data, nuint count, ref float value) nothrow
    {
        uint bits = (uint)0;
        bool success = TryReadUInt32LittleEndian(data, count, ref bits);
        value = *((float*)&bits);
        return success;
    }

    public static bool TryWriteFloatLittleEndian(byte* data, nuint count, float value) nothrow
        => TryWriteUInt32LittleEndian(data, count, *((uint*)&value));

    public static bool TryReadFloatBigEndian(readonly(byte)* data, nuint count, ref float value) nothrow
    {
        uint bits = (uint)0;
        bool success = TryReadUInt32BigEndian(data, count, ref bits);
        value = *((float*)&bits);
        return success;
    }

    public static bool TryWriteFloatBigEndian(byte* data, nuint count, float value) nothrow
        => TryWriteUInt32BigEndian(data, count, *((uint*)&value));

    public static bool TryReadDoubleLittleEndian(readonly(byte)* data, nuint count, ref double value) nothrow
    {
        ulong bits = (ulong)0;
        bool success = TryReadUInt64LittleEndian(data, count, ref bits);
        value = *((double*)&bits);
        return success;
    }

    public static bool TryWriteDoubleLittleEndian(byte* data, nuint count, double value) nothrow
        => TryWriteUInt64LittleEndian(data, count, *((ulong*)&value));

    public static bool TryReadDoubleBigEndian(readonly(byte)* data, nuint count, ref double value) nothrow
    {
        ulong bits = (ulong)0;
        bool success = TryReadUInt64BigEndian(data, count, ref bits);
        value = *((double*)&bits);
        return success;
    }

    public static bool TryWriteDoubleBigEndian(byte* data, nuint count, double value) nothrow
        => TryWriteUInt64BigEndian(data, count, *((ulong*)&value));

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
