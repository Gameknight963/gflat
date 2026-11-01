using Standard::Collections;
using Standard::Text;

namespace Standard::Csv;

// Reuses owning field slots. On failure only fully decoded fields remain.
public bool TryReadRecord(CsvReader* input, ref Vector<String> output)
{
    if (input.HasValue && !input.EndOfRecord)
        throw new Exception("CsvReader is between fields of a record");
    nuint count = 0;
    try
    {
        while (input.Read())
        {
            if (count < output.Count) CsvReaderText::CopyValue(input, ref output[count]);
            else
            {
                String field = new();
                CsvReaderText::CopyValue(input, ref field);
                output.Add(move field);
            }
            count++;
            if (input.EndOfRecord)
            {
                output.Truncate(count);
                return true;
            }
        }
    }
    catch
    {
        output.Truncate(count);
        throw;
    }
    output.Clear();
    return false;
}

extension CsvReader
{
    public bool TryReadRecord(ref Vector<String> output)
        => Standard::Csv::TryReadRecord(this, ref output);
}
