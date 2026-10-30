using Standard::Memory;
namespace Standard::Csv;

// Borrows complete counted byte input. Read emits one field, not one record.
public struct CsvReader
{
    private readonly(char)* input;
    private nuint length;
    private nuint position;
    private bool done;
    private bool failed;
    private bool valid;
    private nuint offset;
    private nuint rawLength;
    private nuint valueLength;
    private bool quoted;
    private bool endOfRecord;

    public CsvReader(readonly(char)* data, nuint count)
    {
        input = data;
        length = count;
        position = 0;
        done = count == 0;
        failed = false;
        valid = false;
        offset = 0;
        rawLength = 0;
        valueLength = 0;
        quoted = false;
        endOfRecord = false;
    }

    public bool HasValue { immutable get nothrow => valid; }
    public nuint FieldOffset { immutable get nothrow => offset; }
    public nuint ValueLength { immutable get nothrow => valueLength; }
    public bool IsQuoted { immutable get nothrow => quoted; }
    public bool EndOfRecord { immutable get nothrow => endOfRecord; }

    public Span<readonly(char)> RawField
    {
        immutable get
        {
            if (!valid || rawLength == 0) return new Span<readonly(char)>();
            return new Span<readonly(char)>(input + offset, rawLength);
        }
    }

    public void Reset()
    {
        position = 0;
        done = length == 0;
        failed = false;
        valid = false;
        offset = 0;
        rawLength = 0;
        valueLength = 0;
        quoted = false;
        endOfRecord = false;
    }

    private void Fail(readonly(char)* message, nuint errorOffset)
    {
        throw new CsvParseException(message, errorOffset);
    }

    public bool Read()
    {
        if (failed) this.Fail("Reset the failed CsvReader before reading again", position);
        valid = false;
        offset = 0;
        rawLength = 0;
        valueLength = 0;
        quoted = false;
        endOfRecord = false;
        if (done) return false;
        failed = true;
        nuint start = position;
        nuint decoded = 0;
        bool isQuoted = position < length && input[position] == '"';
        if (isQuoted)
        {
            position++;
            bool closed = false;
            while (position < length)
            {
                char value = input[position++];
                if (value == '"')
                {
                    if (position < length && input[position] == '"') position++;
                    else
                    {
                        closed = true;
                        break;
                    }
                }
                decoded++;
            }
            if (!closed) this.Fail("Unterminated CSV field", length);
        }
        else
        {
            while (position < length)
            {
                char value = input[position];
                if (value == ',' || value == '\r' || value == '\n') break;
                if (value == '"') this.Fail("Quote inside an unquoted CSV field", position);
                position++;
                decoded++;
            }
        }
        nuint end = position;
        bool last = true;
        if (position == length) done = true;
        else
        {
            char separator = input[position];
            if (separator == ',')
            {
                position++;
                last = false;
            }
            else if (separator == '\n')
            {
                position++;
                done = position == length;
            }
            else if (separator == '\r')
            {
                position++;
                if (position == length || input[position] != '\n')
                    this.Fail("Expected LF after CSV CR", position);
                position++;
                done = position == length;
            }
            else this.Fail("Expected CSV delimiter after a quoted field", position);
        }
        offset = start;
        rawLength = end - start;
        valueLength = decoded;
        quoted = isQuoted;
        endOfRecord = last;
        valid = true;
        failed = false;
        return true;
    }

    // Destination and written storage must be separate from input and this reader.
    // written must also be separate from the destination byte buffer.
    // Output is counted bytes, without a terminator; input remains unchanged.
    public immutable bool TryCopyValue(char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        if (!valid || valueLength > capacity) return false;
        nuint start = offset;
        nuint end = offset + rawLength;
        if (quoted)
        {
            start++;
            end--;
        }
        nuint count = 0;
        while (start < end)
        {
            char value = input[start++];
            if (quoted && value == '"') start++;
            output[count++] = value;
        }
        written = count;
        return true;
    }
}
