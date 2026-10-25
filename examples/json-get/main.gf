using std;

int main(int argc, char** argv)
{
    bool pointerFile = false;
    if (argc >= 3) pointerFile = CString::Equals(argv[2], "--pointer-file");
    if ((pointerFile && argc != 4) || (!pointerFile && argc != 3))
    {
        Console::Error.WriteLine("Usage: json-get <input-file> <pointer>");
        Console::Error.WriteLine("       json-get <input-file> --pointer-file <utf8-file>");
        return 2;
    }
    try
    {
        String document = FileText::ReadAll(argv[1]);
        Span<readonly(char)> input = new(document.Data, document.Length);
        String pointerText = new();
        Span<readonly(char)> pointer = new(argv[2], CString::Length(argv[2]));
        if (pointerFile)
        {
            pointerText = FileText::ReadAll(argv[3]);
            pointer = new(pointerText.Data, pointerText.Length);
        }
        Span<readonly(char)> value = new();
        if (!JsonPointer::TryResolve(input, pointer, ref value))
        {
            Console::Error.WriteLine("JSON pointer did not resolve");
            return 2;
        }
        Console::WriteLine(value.Data!, value.Length);
        Console::Flush();
    }
    catch (ref JsonParseException error)
    {
        Console::Error.Write("Invalid JSON at byte ", 21);
        error.Offset.Format(Console::Error);
        Console::Error.Write(": ", 2);
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
    catch (ref Exception error)
    {
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
    return 0;
}
