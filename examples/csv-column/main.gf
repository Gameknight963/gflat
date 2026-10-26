using std::Text;
using std::Csv;
using std::IO;
using std;

int main(int argc, char** argv)
{
    nuint selected = 0;
    if (argc != 4 || !TryParseNUInt(argv[2], &selected))
    {
        Console::Error.WriteLine("Usage: csv-column <input> <zero-based-column-index> <output>");
        return 2;
    }
    bool writingOutput = false;
    try
    {
        String input = FileText::ReadAll(argv[1]);
        CsvReader reader = new(input.Data, input.Length);
        String field = new();
        nuint column = 0;
        // Validate every field and size the selected-field buffer before opening output.
        while (reader.Read())
        {
            if (column == selected) CsvReaderText::CopyValue(&reader, ref field);
            if (reader.EndOfRecord)
            {
                if (column < selected) throw new Exception("Selected column is missing from a record", 2);
                column = 0;
            }
            else
            {
                if (column == (nuint)0 - 1) throw new Exception("CSV column index overflow", 2);
                column++;
            }
        }

        reader.Reset();
        column = 0;
        writingOutput = true;
        File output = File::OpenWrite(argv[3]);
        CsvWriter writer = new(&output);
        while (reader.Read())
        {
            if (column == selected)
            {
                CsvReaderText::CopyValue(&reader, ref field);
                writer.WriteField(field.Data, field.Length);
                writer.WriteEndRecord();
            }
            if (reader.EndOfRecord) column = 0;
            else column++;
        }
        output.Close();
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
        if (writingOutput) Console::Error.WriteLine("Unable to write CSV column");
        else Console::Error.WriteLine(error.GetMessage());
        if (!writingOutput && error.GetCode() == 2) return 2;
        return 1;
    }
}
