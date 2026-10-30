using Standard::Text;
using Standard::Json;
using Standard::IO;
using Standard;

int main(int argc, char** argv)
{
    if (argc != 2 && argc != 3)
    {
        Console::Error.WriteLine("Usage: json-pretty <input-file> [indent-spaces]");
        return 2;
    }
    uint indentSpaces = 2u;
    if (argc == 3)
    {
        if (!TryParseUInt(argv[2], &indentSpaces) || indentSpaces > 16u)
        {
            Console::Error.WriteLine("json-pretty: indent-spaces must be a decimal integer from 0 through 16");
            return 2;
        }
    }
    try
    {
        String document = FileText::ReadAll(argv[1]);
        JsonText::PrettyPrint(document.Data, document.Length, Console::Out, (nuint)indentSpaces);
        Console::WriteLine();
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
