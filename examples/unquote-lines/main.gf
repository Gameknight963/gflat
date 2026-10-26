using std::Json;
using std::IO;
using std::Memory;
using std;

// Decode in place: validation completes before JsonString writes any bytes.
int main(int argc, char** argv)
{
    if (argc != 2)
    {
        Console::Error.WriteLine("Usage: unquote-lines <input-file>");
        return 2;
    }
    nuint lineNumber = 0;
    bool decodingLine = false;
    try
    {
        File input = File::OpenRead(argv[1]);
        String line = s"";
        while (true)
        {
            line.Clear();
            if (!FileLines::TryReadLine(&input, &line)) break;
            lineNumber++;
            decodingLine = true;
            nuint decoded = 0;
            if (line.Length < 2) throw new Exception("Expected one complete JSON string");
            {
                Span<char> bytes = line.AsSpan();
                if (!JsonString::TryUnquote(bytes.Data!, bytes.Length, bytes.Data!, bytes.Length, ref decoded))
                    throw new Exception("Expected one complete JSON string with valid UTF-8 and escapes");
            }
            line.Remove(decoded, line.Length - decoded);
            decodingLine = false;
            Console::WriteLine(line.Data, line.Length);
        }
        input.Close();
        Console::Flush();
    }
    catch (ref Exception error)
    {
        Console::Error.Write(argv[1], CString::Length(argv[1]));
        if (decodingLine)
        {
            Console::Error.Write(": line ", 7);
            lineNumber.Format(Console::Error);
        }
        Console::Error.Write(": ", 2);
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
    return 0;
}
