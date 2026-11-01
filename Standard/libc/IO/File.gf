using Standard::Collections;
using Standard::Memory;
using Standard::Text;

namespace Standard::IO;

// Whole-file operations and stream creation by path.
public static class File
{
    // Follows links. Missing paths, other kinds, and metadata failures return false.
    public static bool Exists(readonly(char)* path) nothrow
    {
        if (path[0] == '\0') return false;
        return file_path_detail::__gflat_stdio_path_kind(path) == 1;
    }

    public static FileStream OpenRead(readonly(char)* path) => FileStream::OpenRead(path);
    public static FileStream OpenWrite(readonly(char)* path) => FileStream::OpenWrite(path);
    public static FileStream CreateNew(readonly(char)* path) => FileStream::CreateNew(path);
    public static FileStream OpenAppend(readonly(char)* path) => FileStream::OpenAppend(path);
    public static FileStream OpenTemporary() => FileStream::OpenTemporary();

    public static void Remove(readonly(char)* path)
    {
        if (file_path_detail::remove(path) != 0) throw new IOException("File removal failed");
    }

    public static void Rename(readonly(char)* source, readonly(char)* destination)
    {
        if (file_path_detail::rename(source, destination) != 0) throw new IOException("File rename failed");
    }

    // Reads the file bytes into an owning String, preserving embedded zeros and line endings.
    public static String ReadAllText(readonly(char)* path)
        => File::ReadAllText(path, (nuint)0 - (nuint)1);

    public static String ReadAllText(readonly(char)* path, nuint maximumBytes)
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

    public static void WriteAllText(readonly(char)* path, readonly(char)* data, nuint count)
    {
        FileStream output = FileStream::OpenWrite(path);
        output.Write(data, count);
        output.Close();
    }

    public static void WriteAllText(readonly(char)* path, readonly(char)* text)
    {
        FileStream output = FileStream::OpenWrite(path);
        output.Write(text);
        output.Close();
    }

    public static void WriteAllText(readonly(char)* path, ref immutable(String) text)
    {
        File::WriteAllText(path, text.Data, text.Length);
    }

    public static void AppendAllText(readonly(char)* path, readonly(char)* data, nuint count)
    {
        FileStream output = FileStream::OpenAppend(path);
        output.Write(data, count);
        output.Close();
    }

    public static void AppendAllText(readonly(char)* path, readonly(char)* text)
    {
        FileStream output = FileStream::OpenAppend(path);
        output.Write(text);
        output.Close();
    }

    public static void AppendAllText(readonly(char)* path, ref immutable(String) text)
    {
        File::AppendAllText(path, text.Data, text.Length);
    }

    // Splits LF and CRLF lines, retaining bare CR and embedded zero bytes.
    // A final line ending terminates its line without adding another empty line.
    public static Vector<String> ReadAllLines(readonly(char)* path)
    {
        FileStream input = FileStream::OpenRead(path);
        Vector<String> lines = new Vector<String>();
        while (true)
        {
            String line = new String();
            if (!FileLines::TryReadLine(&input, &line)) break;
            lines.Add(move line);
        }
        input.Close();
        return move lines;
    }

    // Writes each counted String followed by LF, including the last String.
    public static void WriteAllLines(readonly(char)* path, Span<immutable(String)> lines)
    {
        FileStream output = FileStream::OpenWrite(path);
        File::WriteLines(&output, lines);
        output.Close();
    }

    public static void WriteAllLines(readonly(char)* path, immutable(String)* lines, nuint count)
        => File::WriteAllLines(path, new Span<immutable(String)>(lines, count));

    public static void WriteAllLines(readonly(char)* path, ref immutable(Vector<String>) lines)
        => File::WriteAllLines(path, lines.AsImmutable());

    public static void AppendAllLines(readonly(char)* path, Span<immutable(String)> lines)
    {
        FileStream output = FileStream::OpenAppend(path);
        File::WriteLines(&output, lines);
        output.Close();
    }

    public static void AppendAllLines(readonly(char)* path, immutable(String)* lines, nuint count)
        => File::AppendAllLines(path, new Span<immutable(String)>(lines, count));

    public static void AppendAllLines(readonly(char)* path, ref immutable(Vector<String>) lines)
        => File::AppendAllLines(path, lines.AsImmutable());

    private static void WriteLines(FileStream* output, Span<immutable(String)> lines)
    {
        for (nuint index = 0; index < lines.Length; index++)
            output.WriteLine(lines[index].Data, lines[index].Length);
    }

    public static Vector<byte> ReadAllBytes(readonly(char)* path)
        => File::ReadAllBytes(path, (nuint)0 - (nuint)1);

    public static Vector<byte> ReadAllBytes(readonly(char)* path, nuint maximumBytes)
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

    public static void WriteAllBytes(readonly(char)* path, readonly(byte)* data, nuint count)
    {
        FileStream output = FileStream::OpenWrite(path);
        output.Write(data.reinterpret<readonly(char)*>(), count);
        output.Close();
    }

    public static void AppendAllBytes(readonly(char)* path, readonly(byte)* data, nuint count)
    {
        FileStream output = FileStream::OpenAppend(path);
        output.Write(data.reinterpret<readonly(char)*>(), count);
        output.Close();
    }

    public static void WriteAllBytes(readonly(char)* path, ref immutable(Vector<byte>) bytes)
    {
        FileStream output = FileStream::OpenWrite(path);
        if (bytes.Count != 0)
        {
            Span<immutable(byte)> view = bytes.AsImmutable();
            output.Write((view.Data!).reinterpret<readonly(char)*>(), view.Length);
        }
        output.Close();
    }

    public static void AppendAllBytes(readonly(char)* path, ref immutable(Vector<byte>) bytes)
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
    private extern int __gflat_stdio_path_kind(readonly(char)* path) nothrow;
    private extern int remove(readonly(char)* path);
    private extern int rename(readonly(char)* source, readonly(char)* destination);
}
