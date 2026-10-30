namespace std::IO;

// File-specific entry points for the core binary IO extensions.
// No ownership transfer, close, flush or rewind is performed.
public static class FileBinary
{
    public static ushort ReadUInt16LittleEndian(File* input)
        => global::std::IO::ReadUInt16LittleEndian(input);
    public static void WriteUInt16LittleEndian(File* output, ushort value)
    {
        global::std::IO::WriteUInt16LittleEndian(output, value);
    }

    public static ushort ReadUInt16BigEndian(File* input)
        => global::std::IO::ReadUInt16BigEndian(input);
    public static void WriteUInt16BigEndian(File* output, ushort value)
    {
        global::std::IO::WriteUInt16BigEndian(output, value);
    }

    public static uint ReadUInt32LittleEndian(File* input)
        => global::std::IO::ReadUInt32LittleEndian(input);
    public static void WriteUInt32LittleEndian(File* output, uint value)
    {
        global::std::IO::WriteUInt32LittleEndian(output, value);
    }

    public static uint ReadUInt32BigEndian(File* input)
        => global::std::IO::ReadUInt32BigEndian(input);
    public static void WriteUInt32BigEndian(File* output, uint value)
    {
        global::std::IO::WriteUInt32BigEndian(output, value);
    }

    public static ulong ReadUInt64LittleEndian(File* input)
        => global::std::IO::ReadUInt64LittleEndian(input);
    public static void WriteUInt64LittleEndian(File* output, ulong value)
    {
        global::std::IO::WriteUInt64LittleEndian(output, value);
    }

    public static ulong ReadUInt64BigEndian(File* input)
        => global::std::IO::ReadUInt64BigEndian(input);
    public static void WriteUInt64BigEndian(File* output, ulong value)
    {
        global::std::IO::WriteUInt64BigEndian(output, value);
    }

    public static short ReadInt16LittleEndian(File* input)
        => global::std::IO::ReadInt16LittleEndian(input);
    public static void WriteInt16LittleEndian(File* output, short value)
    {
        global::std::IO::WriteInt16LittleEndian(output, value);
    }

    public static short ReadInt16BigEndian(File* input)
        => global::std::IO::ReadInt16BigEndian(input);
    public static void WriteInt16BigEndian(File* output, short value)
    {
        global::std::IO::WriteInt16BigEndian(output, value);
    }

    public static int ReadInt32LittleEndian(File* input)
        => global::std::IO::ReadInt32LittleEndian(input);
    public static void WriteInt32LittleEndian(File* output, int value)
    {
        global::std::IO::WriteInt32LittleEndian(output, value);
    }

    public static int ReadInt32BigEndian(File* input)
        => global::std::IO::ReadInt32BigEndian(input);
    public static void WriteInt32BigEndian(File* output, int value)
    {
        global::std::IO::WriteInt32BigEndian(output, value);
    }

    public static long ReadInt64LittleEndian(File* input)
        => global::std::IO::ReadInt64LittleEndian(input);
    public static void WriteInt64LittleEndian(File* output, long value)
    {
        global::std::IO::WriteInt64LittleEndian(output, value);
    }

    public static long ReadInt64BigEndian(File* input)
        => global::std::IO::ReadInt64BigEndian(input);
    public static void WriteInt64BigEndian(File* output, long value)
    {
        global::std::IO::WriteInt64BigEndian(output, value);
    }

    public static float ReadFloatLittleEndian(File* input)
        => global::std::IO::ReadFloatLittleEndian(input);
    public static void WriteFloatLittleEndian(File* output, float value)
    {
        global::std::IO::WriteFloatLittleEndian(output, value);
    }

    public static float ReadFloatBigEndian(File* input)
        => global::std::IO::ReadFloatBigEndian(input);
    public static void WriteFloatBigEndian(File* output, float value)
    {
        global::std::IO::WriteFloatBigEndian(output, value);
    }

    public static double ReadDoubleLittleEndian(File* input)
        => global::std::IO::ReadDoubleLittleEndian(input);
    public static void WriteDoubleLittleEndian(File* output, double value)
    {
        global::std::IO::WriteDoubleLittleEndian(output, value);
    }

    public static double ReadDoubleBigEndian(File* input)
        => global::std::IO::ReadDoubleBigEndian(input);
    public static void WriteDoubleBigEndian(File* output, double value)
    {
        global::std::IO::WriteDoubleBigEndian(output, value);
    }

    public static extralong ReadExtraLongLittleEndian(File* input)
        => global::std::IO::ReadExtraLongLittleEndian(input);
    public static void WriteExtraLongLittleEndian(File* output, extralong value)
    {
        global::std::IO::WriteExtraLongLittleEndian(output, value);
    }

    public static extralong ReadExtraLongBigEndian(File* input)
        => global::std::IO::ReadExtraLongBigEndian(input);
    public static void WriteExtraLongBigEndian(File* output, extralong value)
    {
        global::std::IO::WriteExtraLongBigEndian(output, value);
    }

}
