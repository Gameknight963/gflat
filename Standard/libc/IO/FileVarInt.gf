using Standard::Binary;
namespace Standard::IO;

// Optional canonical base-128 integers on borrowed File streams.
public static class FileVarInt
{
    public static uint ReadUInt32(File* input)
    {
        byte[5] bytes;
        nuint count = FileVarInt::ReadBytes(input, bytes, 5);
        uint value = 0u;
        nuint consumed = 0;
        if (VarInt::ReadUInt32(bytes, count, ref value, ref consumed) != VarInt::Status::Done)
            throw new IOException("Invalid UInt32 variable-length integer");
        return value;
    }

    public static ulong ReadUInt64(File* input)
    {
        byte[10] bytes;
        nuint count = FileVarInt::ReadBytes(input, bytes, 10);
        ulong value = 0ul;
        nuint consumed = 0;
        if (VarInt::ReadUInt64(bytes, count, ref value, ref consumed) != VarInt::Status::Done)
            throw new IOException("Invalid UInt64 variable-length integer");
        return value;
    }

    private static nuint ReadBytes(File* input, byte* bytes, nuint maximum)
    {
        for (nuint index = 0; index < maximum; index++)
        {
            int value = input.ReadByte();
            if (value == -1) throw new EndOfStreamException();
            bytes[index] = (byte)value;
            if (value < 128) return index + 1;
        }
        return maximum;
    }

    public static void WriteUInt32(File* output, uint value)
        => FileVarInt::WriteUInt64(output, (ulong)value);

    public static void WriteUInt64(File* output, ulong value)
    {
        byte[10] bytes;
        nuint written = 0;
        VarInt::TryWriteUInt64(bytes, 10, value, ref written);
        output.Write(((byte*)bytes).reinterpret<char*>(), written);
    }
}
