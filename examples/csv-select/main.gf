using Standard;
using Standard::Collections;
using Standard::Csv;
using Standard::IO;
using Standard::Text;

int main(int argc, char** argv)
{
    if (argc < 4)
    {
        Console::Error.WriteLine("Usage: csv-select <input> <output> <column-name>...");
        return 2;
    }
    try
    {
        const nuint MaximumInputBytes = 67108864; // 64 MiB
        if (File::GetLength(argv[1]) > (long)MaximumInputBytes)
            throw new Exception("CSV input exceeds 64 MiB", 2);
        String input = File::ReadAllText(argv[1], MaximumInputBytes);
        CsvReader reader = new(input.Data, input.Length);
        Vector<String> row = new();
        if (!reader.TryReadRecord(ref row)) throw new Exception("CSV header is missing", 2);
        nuint width = row.Count;
        Vector<nuint> selected = new();
        selected.Reserve((nuint)(argc - 3));
        for (int argument = 3; argument < argc; argument++)
        {
            nuint found = width;
            for (nuint column = 0; column < width; column++)
            {
                if (!row[column].Equals(argv[argument])) continue;
                if (found != width) throw new Exception("Selected header is ambiguous", 2);
                found = column;
            }
            if (found == width) throw new Exception("Selected header was not found", 2);
            selected.Add(found);
        }
        while (reader.TryReadRecord(ref row))
            if (row.Count != width) throw new Exception("CSV record width differs from its header", 2);

        reader.Reset();
        FileStream output = File::CreateNew(argv[2]);
        CsvWriter writer = new(&output);
        while (reader.TryReadRecord(ref row))
        {
            for (nuint index = 0; index < selected.Count; index++)
            {
                nuint column = selected[index];
                writer.WriteField(row[column].Data, row[column].Length);
            }
            writer.WriteEndRecord();
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
        Console::Error.WriteLine(error.GetMessage());
        if (error.GetCode() == 2) return 2;
        return 1;
    }
}
