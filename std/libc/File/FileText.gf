namespace std;

// Owning String conveniences. File.gf itself remains usable without String.
public static class FileText
{
    // Reads raw UTF-8 bytes; no decoding, BOM removal, or newline conversion.
    // Embedded null bytes are preserved in String.Length.
    public static String ReadAll(readonly(char*) path)
    {
        File input = File::OpenRead(path);
        String text = new String();
        char[4096] buffer;
        while (true)
        {
            nuint count = input.Read(buffer, 4096);
            if (count == 0) break;
            text.AppendLiteral(buffer, count);
        }
        input.Close();
        return move text;
    }

    public static void WriteAll(readonly(char*) path, readonly(char*) data, nuint count)
    {
        File output = File::OpenWrite(path);
        output.Write(data, count);
        output.Close();
    }

    public static void WriteAll(readonly(char*) path, readonly(char*) text)
    {
        File output = File::OpenWrite(path);
        output.Write(text);
        output.Close();
    }

    public static void WriteAll(readonly(char*) path, ref readonly(String) text)
    {
        FileText::WriteAll(path, text.Data, text.Length);
    }

    public static void AppendAll(readonly(char*) path, readonly(char*) data, nuint count)
    {
        File output = File::OpenAppend(path);
        output.Write(data, count);
        output.Close();
    }

    public static void AppendAll(readonly(char*) path, readonly(char*) text)
    {
        File output = File::OpenAppend(path);
        output.Write(text);
        output.Close();
    }

    public static void AppendAll(readonly(char*) path, ref readonly(String) text)
    {
        FileText::AppendAll(path, text.Data, text.Length);
    }
}
