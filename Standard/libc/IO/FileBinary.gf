namespace Standard::IO;

// FileStream-specific entry points for the core binary IO extensions.
// No ownership transfer, close, flush or rewind is performed.
public static class FileBinary
{
    public static ushort ReadUInt16LittleEndian(FileStream* input)
        => global::Standard::IO::ReadUInt16LittleEndian(input);
    public static void WriteUInt16LittleEndian(FileStream* output, ushort value)
    {
        global::Standard::IO::WriteUInt16LittleEndian(output, value);
    }

    public static ushort ReadUInt16BigEndian(FileStream* input)
        => global::Standard::IO::ReadUInt16BigEndian(input);
    public static void WriteUInt16BigEndian(FileStream* output, ushort value)
    {
        global::Standard::IO::WriteUInt16BigEndian(output, value);
    }

    public static uint ReadUInt32LittleEndian(FileStream* input)
        => global::Standard::IO::ReadUInt32LittleEndian(input);
    public static void WriteUInt32LittleEndian(FileStream* output, uint value)
    {
        global::Standard::IO::WriteUInt32LittleEndian(output, value);
    }

    public static uint ReadUInt32BigEndian(FileStream* input)
        => global::Standard::IO::ReadUInt32BigEndian(input);
    public static void WriteUInt32BigEndian(FileStream* output, uint value)
    {
        global::Standard::IO::WriteUInt32BigEndian(output, value);
    }

    public static ulong ReadUInt64LittleEndian(FileStream* input)
        => global::Standard::IO::ReadUInt64LittleEndian(input);
    public static void WriteUInt64LittleEndian(FileStream* output, ulong value)
    {
        global::Standard::IO::WriteUInt64LittleEndian(output, value);
    }

    public static ulong ReadUInt64BigEndian(FileStream* input)
        => global::Standard::IO::ReadUInt64BigEndian(input);
    public static void WriteUInt64BigEndian(FileStream* output, ulong value)
    {
        global::Standard::IO::WriteUInt64BigEndian(output, value);
    }

    public static short ReadInt16LittleEndian(FileStream* input)
        => global::Standard::IO::ReadInt16LittleEndian(input);
    public static void WriteInt16LittleEndian(FileStream* output, short value)
    {
        global::Standard::IO::WriteInt16LittleEndian(output, value);
    }

    public static short ReadInt16BigEndian(FileStream* input)
        => global::Standard::IO::ReadInt16BigEndian(input);
    public static void WriteInt16BigEndian(FileStream* output, short value)
    {
        global::Standard::IO::WriteInt16BigEndian(output, value);
    }

    public static int ReadInt32LittleEndian(FileStream* input)
        => global::Standard::IO::ReadInt32LittleEndian(input);
    public static void WriteInt32LittleEndian(FileStream* output, int value)
    {
        global::Standard::IO::WriteInt32LittleEndian(output, value);
    }

    public static int ReadInt32BigEndian(FileStream* input)
        => global::Standard::IO::ReadInt32BigEndian(input);
    public static void WriteInt32BigEndian(FileStream* output, int value)
    {
        global::Standard::IO::WriteInt32BigEndian(output, value);
    }

    public static long ReadInt64LittleEndian(FileStream* input)
        => global::Standard::IO::ReadInt64LittleEndian(input);
    public static void WriteInt64LittleEndian(FileStream* output, long value)
    {
        global::Standard::IO::WriteInt64LittleEndian(output, value);
    }

    public static long ReadInt64BigEndian(FileStream* input)
        => global::Standard::IO::ReadInt64BigEndian(input);
    public static void WriteInt64BigEndian(FileStream* output, long value)
    {
        global::Standard::IO::WriteInt64BigEndian(output, value);
    }

    public static float ReadFloatLittleEndian(FileStream* input)
        => global::Standard::IO::ReadFloatLittleEndian(input);
    public static void WriteFloatLittleEndian(FileStream* output, float value)
    {
        global::Standard::IO::WriteFloatLittleEndian(output, value);
    }

    public static float ReadFloatBigEndian(FileStream* input)
        => global::Standard::IO::ReadFloatBigEndian(input);
    public static void WriteFloatBigEndian(FileStream* output, float value)
    {
        global::Standard::IO::WriteFloatBigEndian(output, value);
    }

    public static double ReadDoubleLittleEndian(FileStream* input)
        => global::Standard::IO::ReadDoubleLittleEndian(input);
    public static void WriteDoubleLittleEndian(FileStream* output, double value)
    {
        global::Standard::IO::WriteDoubleLittleEndian(output, value);
    }

    public static double ReadDoubleBigEndian(FileStream* input)
        => global::Standard::IO::ReadDoubleBigEndian(input);
    public static void WriteDoubleBigEndian(FileStream* output, double value)
    {
        global::Standard::IO::WriteDoubleBigEndian(output, value);
    }

    public static extralong ReadExtraLongLittleEndian(FileStream* input)
        => global::Standard::IO::ReadExtraLongLittleEndian(input);
    public static void WriteExtraLongLittleEndian(FileStream* output, extralong value)
    {
        global::Standard::IO::WriteExtraLongLittleEndian(output, value);
    }

    public static extralong ReadExtraLongBigEndian(FileStream* input)
        => global::Standard::IO::ReadExtraLongBigEndian(input);
    public static void WriteExtraLongBigEndian(FileStream* output, extralong value)
    {
        global::Standard::IO::WriteExtraLongBigEndian(output, value);
    }

}
