namespace std;

// Optional owning binary conveniences. No text encoding or terminator is added.
public static class FileBytes
{
    public static Vector<byte> ReadAll(immutable(char*) path)
    {
        File input = File::OpenRead(path);
        Vector<byte> bytes = new Vector<byte>();
        char[4096] buffer;
        while (true)
        {
            nuint count = input.Read(buffer, 4096);
            if (count == 0) break;
            // Vector grows geometrically; do not reserve the exact size per chunk.
            for (nuint i = 0; i < count; i++) bytes.Add((byte)buffer[i]);
        }
        input.Close();
        return move bytes;
    }

    public static void WriteAll(immutable(char*) path, readonly(byte)* data, nuint count)
    {
        File output = File::OpenWrite(path);
        output.Write((readonly(char)*)data, count);
        output.Close();
    }

    public static void AppendAll(immutable(char*) path, readonly(byte)* data, nuint count)
    {
        File output = File::OpenAppend(path);
        output.Write((readonly(char)*)data, count);
        output.Close();
    }
}
