using std::IO;
using std;

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        Console::Error.WriteLine("Usage: json-minify <input-file>");
        return 2;
    }
    try
    {
        String document = FileText::ReadAll(argv[1]);
        JsonText::Minify(document.Data, document.Length, Console::Out);
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
