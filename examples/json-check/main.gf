using std;

// Keep the complete file alive while JsonReader borrows its counted bytes.
int main(int argc, char** argv)
{
    if (argc != 2)
    {
        Console::Error.WriteLine("Usage: json-check <input-file>");
        return 2;
    }
    try
    {
        String document = FileText::ReadAll(argv[1]);
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
        Console::Error.Write(argv[1], CString::Length(argv[1])); Console::Error.Write(": byte ", 7);
        error.Offset.Format(Console::Error);
        Console::Error.Write(": ", 2); Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
    catch (ref Exception error)
    {
        Console::Error.Write(argv[1], CString::Length(argv[1])); Console::Error.Write(": ", 2);
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
    return 0;
}
