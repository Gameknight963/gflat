namespace std;

// Optional owning output; CsvReader itself does not depend on String.
public static class CsvReaderText
{
    // Output storage must be separate from the reader and its entire input.
    public static void CopyValue(immutable(CsvReader)* reader, ref String output)
    {
        if (!reader.HasValue) throw new Exception("CsvReader has no current field");
        output.Resize(reader.ValueLength);
        if (output.IsEmpty) return;
        Span<char> buffer = output.AsSpan();
        nuint written = 0;
        if (!reader.TryCopyValue(buffer.Data!, buffer.Length, ref written))
            throw new Exception("Unable to decode CSV field");
    }
}
