using std::Memory;
using std::Collections;
namespace std::IO;

// Optional owning binary conveniences. No text encoding or terminator is added.
public static class FileBytes
{
    public static Vector<byte> ReadAll(immutable(char*) path)
        => FileBytes::ReadAll(path, (nuint)0 - (nuint)1);

    public static Vector<byte> ReadAll(immutable(char*) path, nuint maximumBytes)
    {
        File input = File::OpenRead(path);
        Vector<byte> bytes = new Vector<byte>();
        char[4096] buffer;
        while (true)
        {
            nuint remaining = maximumBytes - bytes.Count;
            nuint request = 4096;
            if (remaining < 4096) request = remaining + 1;
            nuint count = input.Read(buffer, request);
            if (count == 0) break;
            if (count > remaining) throw new IOException("File exceeds maximum byte count");
            // Vector grows geometrically; do not reserve the exact size per chunk.
            for (nuint i = 0; i < count; i++) bytes.Add((byte)buffer[i]);
        }
        input.Close();
        return move bytes;
    }

    public static void WriteAll(immutable(char*) path, readonly(byte)* data, nuint count)
    {
        File output = File::OpenWrite(path);
        output.Write(data.reinterpret<readonly(char)*>(), count);
        output.Close();
    }

    public static void AppendAll(immutable(char*) path, readonly(byte)* data, nuint count)
    {
        File output = File::OpenAppend(path);
        output.Write(data.reinterpret<readonly(char)*>(), count);
        output.Close();
    }

    public static void WriteAll(immutable(char*) path, ref immutable(Vector<byte>) bytes)
    {
        File output = File::OpenWrite(path);
        if (bytes.Count != 0)
        {
            Span<immutable(byte)> view = bytes.AsImmutable();
            output.Write((view.Data!).reinterpret<readonly(char)*>(), view.Length);
        }
        output.Close();
    }

    public static void AppendAll(immutable(char*) path, ref immutable(Vector<byte>) bytes)
    {
        File output = File::OpenAppend(path);
        if (bytes.Count != 0)
        {
            Span<immutable(byte)> view = bytes.AsImmutable();
            output.Write((view.Data!).reinterpret<readonly(char)*>(), view.Length);
        }
        output.Close();
    }
}
