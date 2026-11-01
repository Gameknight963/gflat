using Standard::Memory;
using Standard::Text;

namespace Standard::Csv;

// Writes one complete record from borrowed String owners. An empty row emits CRLF.
// Row storage and contents must stay stable and separate from destination storage.
public void WriteRecord(CsvWriter* writer, Span<immutable(String)> row)
{
    if (!writer.IsAtRecordBoundary) throw new Exception("CSV writer must be at a record boundary");
    for (nuint index = 0; index < row.Length; index++)
        writer.WriteField(row[index].Data, row[index].Length);
    writer.WriteEndRecord();
}

extension CsvWriter
{
    public void WriteRecord(Span<immutable(String)> row)
        => Standard::Csv::WriteRecord(this, row);
}
