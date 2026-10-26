using std::Csv;
using std::Json;
using std::IO;
using std;

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        Console::Error.WriteLine("Usage: csv-to-json <file>");
        return 2;
    }
    bool writingOutput = false;
    try
    {
        String input = FileText::ReadAll(argv[1]);
        CsvReader reader = new(input.Data, input.Length);
        String field = new();
        // Validate every record and decoded text before sending any JSON to stdout.
        while (reader.Read())
        {
            CsvReaderText::CopyValue(&reader, ref field);
            if (!Utf8::IsValid(field.Data, field.Length))
                throw new Exception("CSV field is not valid UTF-8", 2);
        }

        reader.Reset();
        JsonWriter writer = new(Console::Out);
        bool startRecord = true;
        writingOutput = true;
        writer.WriteStartArray();
        while (reader.Read())
        {
            if (startRecord) writer.WriteStartArray();
            CsvReaderText::CopyValue(&reader, ref field);
            writer.WriteString(field.Data, field.Length);
            startRecord = reader.EndOfRecord;
            if (startRecord) writer.WriteEndArray();
        }
        writer.WriteEndArray();
        writer.Complete();
        Console::WriteLine();
        Console::Flush();
        return 0;
    }
    catch (ref CsvParseException error)
    {
        Console::Error.Write("Invalid CSV at byte ", 20);
        error.Offset.Format(Console::Error);
        Console::Error.Write(": ", 2);
        Console::Error.WriteLine(error.GetMessage());
        return 2;
    }
    catch (ref Exception error)
    {
        if (writingOutput) Console::Error.WriteLine("Unable to write CSV JSON");
        else Console::Error.WriteLine(error.GetMessage());
        if (!writingOutput && error.GetCode() == 2) return 2;
        return 1;
    }
}
