namespace Standard::Csv;

public class CsvParseException : Exception
{
    public readonly(nuint) Offset;

    public CsvParseException(readonly(char)* message, nuint offset) : base(message, 1)
    {
        Offset = offset;
    }
}
