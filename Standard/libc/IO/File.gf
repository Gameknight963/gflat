using Standard::Collections;
using Standard::Memory;
using Standard::Text;

namespace Standard::IO;

// Whole-file operations and stream creation by path.
public static class File
{
    public static FileStream OpenRead(immutable(char*) path) => FileStream::OpenRead(path);
    public static FileStream OpenWrite(immutable(char*) path) => FileStream::OpenWrite(path);
    public static FileStream OpenAppend(immutable(char*) path) => FileStream::OpenAppend(path);
    public static FileStream OpenTemporary() => FileStream::OpenTemporary();

    public static void Remove(immutable(char*) path)
    {
        if (file_path_detail::remove(path) != 0) throw new IOException("File removal failed");
    }

    public static void Rename(immutable(char*) source, immutable(char*) destination)
    {
        if (file_path_detail::rename(source, destination) != 0) throw new IOException("File rename failed");
    }

    // Reads the file bytes into an owning String, preserving embedded zeros and line endings.
    public static String ReadAllText(immutable(char*) path)
        => File::ReadAllText(path, (nuint)0 - (nuint)1);

    public static String ReadAllText(immutable(char*) path, nuint maximumBytes)
    {
        FileStream input = FileStream::OpenRead(path);
        String text = new String();
        char[4096] buffer;
        while (true)
        {
            nuint remaining = maximumBytes - text.Length;
            nuint request = 4096;
            if (remaining < 4096) request = remaining + 1;
            nuint count = input.Read(buffer, request);
            if (count == 0) break;
            if (count > remaining) throw new IOException("File exceeds maximum byte count");
            text.AppendLiteral(buffer, count);
        }
        input.Close();
        return move text;
    }

    public static void WriteAllText(immutable(char*) path, immutable(char*) data, nuint count)
    {
        FileStream output = FileStream::OpenWrite(path);
        output.Write(data, count);
        output.Close();
    }

    public static void WriteAllText(immutable(char*) path, immutable(char*) text)
    {
        FileStream output = FileStream::OpenWrite(path);
        output.Write(text);
        output.Close();
    }

    public static void WriteAllText(immutable(char*) path, ref immutable(String) text)
    {
        File::WriteAllText(path, text.Data, text.Length);
    }

    public static void AppendAllText(immutable(char*) path, immutable(char*) data, nuint count)
    {
        FileStream output = FileStream::OpenAppend(path);
        output.Write(data, count);
        output.Close();
    }

    public static void AppendAllText(immutable(char*) path, immutable(char*) text)
    {
        FileStream output = FileStream::OpenAppend(path);
        output.Write(text);
        output.Close();
    }

    public static void AppendAllText(immutable(char*) path, ref immutable(String) text)
    {
        File::AppendAllText(path, text.Data, text.Length);
    }

    public static Vector<byte> ReadAllBytes(immutable(char*) path)
        => File::ReadAllBytes(path, (nuint)0 - (nuint)1);

    public static Vector<byte> ReadAllBytes(immutable(char*) path, nuint maximumBytes)
    {
        FileStream input = FileStream::OpenRead(path);
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

    public static void WriteAllBytes(immutable(char*) path, readonly(byte)* data, nuint count)
    {
        FileStream output = FileStream::OpenWrite(path);
        output.Write(data.reinterpret<readonly(char)*>(), count);
        output.Close();
    }

    public static void AppendAllBytes(immutable(char*) path, readonly(byte)* data, nuint count)
    {
        FileStream output = FileStream::OpenAppend(path);
        output.Write(data.reinterpret<readonly(char)*>(), count);
        output.Close();
    }

    public static void WriteAllBytes(immutable(char*) path, ref immutable(Vector<byte>) bytes)
    {
        FileStream output = FileStream::OpenWrite(path);
        if (bytes.Count != 0)
        {
            Span<immutable(byte)> view = bytes.AsImmutable();
            output.Write((view.Data!).reinterpret<readonly(char)*>(), view.Length);
        }
        output.Close();
    }

    public static void AppendAllBytes(immutable(char*) path, ref immutable(Vector<byte>) bytes)
    {
        FileStream output = FileStream::OpenAppend(path);
        if (bytes.Count != 0)
        {
            Span<immutable(byte)> view = bytes.AsImmutable();
            output.Write((view.Data!).reinterpret<readonly(char)*>(), view.Length);
        }
        output.Close();
    }
}

namespace file_path_detail
{
    private extern int remove(immutable(char*) path);
    private extern int rename(immutable(char*) source, immutable(char*) destination);
}
