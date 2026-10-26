using std::IO;
using std;

// Keep the complete file alive while JsonReader borrows its counted bytes.
int main(int argc, char** argv)
{
    bool limited = argc > 1 && CString::Equals(argv[1], "--max-bytes");
    if ((limited && argc != 4) || (!limited && argc != 2))
    {
        Console::Error.WriteLine("Usage: json-check [--max-bytes count] <input-file>");
        return 2;
    }
    char* input = argv[1];
    nuint maximumBytes = (nuint)0 - (nuint)1;
    if (limited)
    {
        if (!TryParseNUInt(argv[2], &maximumBytes))
        {
            Console::Error.WriteLine("Usage: json-check [--max-bytes count] <input-file>");
            return 2;
        }
        input = argv[3];
    }
    try
    {
        String document = FileText::ReadAll(input, maximumBytes);
        JsonReader reader = new(document.Data, document.Length);
        nuint tokens = 0;
        nuint maximumNesting = 0;
        while (reader.Read())
        {
            tokens++;
            if (reader.Kind == JsonTokenKind::StartObject || reader.Kind == JsonTokenKind::StartArray)
            {
                nuint nesting = reader.Depth + 1;
                if (nesting > maximumNesting) maximumNesting = nesting;
            }
        }
        Console::Write("Tokens: "); tokens.Format(Console::Out); Console::WriteLine();
        Console::Write("Maximum nesting: "); maximumNesting.Format(Console::Out); Console::WriteLine();
        Console::Flush();
    }
    catch (ref JsonParseException error)
    {
        Console::Error.Write(input, CString::Length(input)); Console::Error.Write(": byte ", 7);
        error.Offset.Format(Console::Error);
        Console::Error.Write(": ", 2); Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
    catch (ref Exception error)
    {
        Console::Error.Write(input, CString::Length(input)); Console::Error.Write(": ", 2);
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
    return 0;
}
