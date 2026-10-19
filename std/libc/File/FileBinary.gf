namespace std;

// Fixed-width unsigned integers with explicit byte order. Borrows the stream.
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
}
