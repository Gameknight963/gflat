namespace std;

// Owning String conveniences. File.gf itself remains usable without String.
public static class FileText
{
    // Reads raw UTF-8 bytes; no decoding, BOM removal, or newline conversion.
    // Embedded null bytes are preserved in String.Length.
    public static String ReadAll(immutable(char*) path)
        => FileText::ReadAll(path, (nuint)0 - (nuint)1);

    public static String ReadAll(immutable(char*) path, nuint maximumBytes)
    {
        File input = File::OpenRead(path);
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

    public static void WriteAll(immutable(char*) path, immutable(char*) data, nuint count)
    {
        File output = File::OpenWrite(path);
        output.Write(data, count);
        output.Close();
    }

    public static void WriteAll(immutable(char*) path, immutable(char*) text)
    {
        File output = File::OpenWrite(path);
        output.Write(text);
        output.Close();
    }

    public static void WriteAll(immutable(char*) path, ref immutable(String) text)
    {
        FileText::WriteAll(path, text.Data, text.Length);
    }

    public static void AppendAll(immutable(char*) path, immutable(char*) data, nuint count)
    {
        File output = File::OpenAppend(path);
        output.Write(data, count);
        output.Close();
    }

    public static void AppendAll(immutable(char*) path, immutable(char*) text)
    {
        File output = File::OpenAppend(path);
        output.Write(text);
        output.Close();
    }

    public static void AppendAll(immutable(char*) path, ref immutable(String) text)
    {
        FileText::AppendAll(path, text.Data, text.Length);
    }
}
