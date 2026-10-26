namespace std::IO;

// Fixed-width integers and IEEE floating-point values with explicit byte order.
// Borrows the stream.
// These operations never depend on host endianness, alignment, or C long width.
public static class FileBinary
{
    public static ushort ReadUInt16LittleEndian(File* input) => (ushort)FileBinary::Read(input, 2, true);
    public static uint ReadUInt32LittleEndian(File* input) => (uint)FileBinary::Read(input, 4, true);
    public static ulong ReadUInt64LittleEndian(File* input) => FileBinary::Read(input, 8, true);
    public static ushort ReadUInt16BigEndian(File* input) => (ushort)FileBinary::Read(input, 2, false);
    public static uint ReadUInt32BigEndian(File* input) => (uint)FileBinary::Read(input, 4, false);
    public static ulong ReadUInt64BigEndian(File* input) => FileBinary::Read(input, 8, false);

    public static void WriteUInt16LittleEndian(File* output, ushort value) { FileBinary::Write(output, (ulong)value, 2, true); }
    public static void WriteUInt32LittleEndian(File* output, uint value) { FileBinary::Write(output, (ulong)value, 4, true); }
    public static void WriteUInt64LittleEndian(File* output, ulong value) { FileBinary::Write(output, value, 8, true); }
    public static void WriteUInt16BigEndian(File* output, ushort value) { FileBinary::Write(output, (ulong)value, 2, false); }
    public static void WriteUInt32BigEndian(File* output, uint value) { FileBinary::Write(output, (ulong)value, 4, false); }
    public static void WriteUInt64BigEndian(File* output, ulong value) { FileBinary::Write(output, value, 8, false); }

    public static short ReadInt16LittleEndian(File* input) => (short)ReadUInt16LittleEndian(input);
    public static void WriteInt16LittleEndian(File* output, short value) { WriteUInt16LittleEndian(output, (ushort)value); }
    public static short ReadInt16BigEndian(File* input) => (short)ReadUInt16BigEndian(input);
    public static void WriteInt16BigEndian(File* output, short value) { WriteUInt16BigEndian(output, (ushort)value); }
    public static int ReadInt32LittleEndian(File* input) => (int)ReadUInt32LittleEndian(input);
    public static void WriteInt32LittleEndian(File* output, int value) { WriteUInt32LittleEndian(output, (uint)value); }
    public static int ReadInt32BigEndian(File* input) => (int)ReadUInt32BigEndian(input);
    public static void WriteInt32BigEndian(File* output, int value) { WriteUInt32BigEndian(output, (uint)value); }
    public static long ReadInt64LittleEndian(File* input) => (long)ReadUInt64LittleEndian(input);
    public static void WriteInt64LittleEndian(File* output, long value) { WriteUInt64LittleEndian(output, (ulong)value); }
    public static long ReadInt64BigEndian(File* input) => (long)ReadUInt64BigEndian(input);
    public static void WriteInt64BigEndian(File* output, long value) { WriteUInt64BigEndian(output, (ulong)value); }

    public static float ReadFloatLittleEndian(File* input)
    {
        uint bits = ReadUInt32LittleEndian(input);
        return *((float*)&bits);
    }
    public static float ReadFloatBigEndian(File* input)
    {
        uint bits = ReadUInt32BigEndian(input);
        return *((float*)&bits);
    }
    public static void WriteFloatLittleEndian(File* output, float value) { WriteUInt32LittleEndian(output, *((uint*)&value)); }
    public static void WriteFloatBigEndian(File* output, float value) { WriteUInt32BigEndian(output, *((uint*)&value)); }

    public static double ReadDoubleLittleEndian(File* input)
    {
        ulong bits = ReadUInt64LittleEndian(input);
        return *((double*)&bits);
    }
    public static double ReadDoubleBigEndian(File* input)
    {
        ulong bits = ReadUInt64BigEndian(input);
        return *((double*)&bits);
    }
    public static void WriteDoubleLittleEndian(File* output, double value) { WriteUInt64LittleEndian(output, *((ulong*)&value)); }
    public static void WriteDoubleBigEndian(File* output, double value) { WriteUInt64BigEndian(output, *((ulong*)&value)); }

    private static ulong Read(File* input, nuint count, bool littleEndian)
    {
        char[8] buffer;
        input.ReadExactly(buffer, count);
        ulong value = 0ul;
        for (nuint i = 0; i < count; i++)
        {
            nuint index = i;
            if (littleEndian) index = count - 1 - i;
            value = (value << 8) | (ulong)(byte)buffer[index];
        }
        return value;
    }

    private static void Write(File* output, ulong value, nuint count, bool littleEndian)
    {
        char[8] buffer;
        for (nuint i = 0; i < count; i++)
        {
            nuint index = i;
            if (!littleEndian) index = count - 1 - i;
            buffer[index] = (char)value;
            value = value >> 8;
        }
        output.Write(buffer, count);
    }

    public static extralong ReadExtraLongLittleEndian(File* input) => ReadExtraLong(input, true);
    public static extralong ReadExtraLongBigEndian(File* input) => ReadExtraLong(input, false);
    public static void WriteExtraLongLittleEndian(File* output, extralong value) { WriteExtraLong(output, value, true); }
    public static void WriteExtraLongBigEndian(File* output, extralong value) { WriteExtraLong(output, value, false); }

    private static extralong ReadExtraLong(File* input, bool littleEndian)
    {
        char[16] buffer;
        input.ReadExactly(buffer, 16);
        extralong value = (extralong)0;
        for (nuint i = 0; i < 16; i++)
        {
            nuint index = i;
            if (littleEndian) index = 15 - i;
            value = (value << 8) | (extralong)(byte)buffer[index];
        }
        return value;
    }

    private static void WriteExtraLong(File* output, extralong value, bool littleEndian)
    {
        char[16] buffer;
        for (nuint i = 0; i < 16; i++)
        {
            nuint index = i;
            if (!littleEndian) index = 15 - i;
            buffer[index] = (char)value;
            value = value >> 8;
        }
        output.Write(buffer, 16);
    }
}
