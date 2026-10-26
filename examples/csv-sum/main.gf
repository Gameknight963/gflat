using std::Text;
using std::Csv;
using std::IO;
using std;

long ReadQuantity(immutable(CsvReader)* field)
{
    char[64] buffer;
    nuint written = 0;
    if (!field.TryCopyValue(buffer, 64, ref written))
        throw new Exception("Numeric field exceeds the 64-byte decoded limit", 2);
    long value = 0l;
    if (!TryParseLong(buffer, written, &value))
        throw new Exception("Expected a signed 64-bit integer in the selected column", 2);
    return value;
}

int main(int argc, char** argv)
{
    nuint selected = 0;
    if (argc != 3 || !TryParseNUInt(argv[2], &selected))
    {
        Console::Error.WriteLine("Usage: csv-sum <file> <zero-based-column>");
        return 2;
    }
    bool writingOutput = false;
    try
    {
        String input = FileText::ReadAll(argv[1]);
        CsvReader reader = new(input.Data, input.Length);
        nuint column = 0;
        bool found = false;
        extralong total = 0;
        while (reader.Read())
        {
            if (column == selected)
            {
                total += (extralong)ReadQuantity(&reader);
                found = true;
            }
            if (reader.EndOfRecord)
            {
                if (!found) throw new Exception("Selected column is missing from a record", 2);
                column = 0;
                found = false;
            }
            else
            {
                if (column == (nuint)0 - (nuint)1)
                    throw new Exception("CSV column index overflow", 2);
                column++;
            }
        }
        writingOutput = true;
        total.Format(Console::Out);
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
        if (writingOutput) Console::Error.WriteLine("Unable to write CSV sum");
        else Console::Error.WriteLine(error.GetMessage());
        return error.GetCode();
    }
}
