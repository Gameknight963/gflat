namespace std;

// Quotes one counted UTF-8 string as JSON, including its surrounding quotes.
public static class JsonString
{
    private static nuint EscapedLength(byte value) nothrow
    {
        if (value == (byte)'"' || value == (byte)'\\') return 2;
        if (value >= (byte)32) return 1;
        if (value == (byte)8 || value == (byte)9 || value == (byte)10 ||
            value == (byte)12 || value == (byte)13) return 2;
        return 6;
    }

    public static bool TryGetQuotedLength(readonly(char)* input, nuint length, ref nuint needed) nothrow
    {
        if (!Utf8::IsValid(input, length)) { needed = 0; return false; }
        nuint total = 2;
        nuint maximum = (nuint)0 - (nuint)1;
        for (nuint i = 0; i < length; i++)
        {
            nuint size = EscapedLength((byte)input[i]);
            if (size > maximum - total) { needed = 0; return false; }
            total += size;
        }
        needed = total;
        return true;
    }

    public static bool TryQuote(readonly(char)* input, nuint length,
        char* output, nuint capacity, ref nuint written) nothrow
    {
        nuint required = 0;
        if (!TryGetQuotedLength(input, length, ref required) || required > capacity)
        { written = 0; return false; }
        output[0] = '"';
        nuint position = 1;
        for (nuint i = 0; i < length; i++)
        {
            byte value = (byte)input[i];
            if (value == (byte)'"' || value == (byte)'\\')
            {
                output[position++] = '\\';
                output[position++] = (char)value;
            }
            else if (value < (byte)32)
            {
                output[position++] = '\\';
                if (value == (byte)8) output[position++] = 'b';
                else if (value == (byte)9) output[position++] = 't';
                else if (value == (byte)10) output[position++] = 'n';
                else if (value == (byte)12) output[position++] = 'f';
                else if (value == (byte)13) output[position++] = 'r';
                else
                {
                    output[position++] = 'u';
                    output[position++] = '0';
                    output[position++] = '0';
                    output[position++] = "0123456789ABCDEF"[(nuint)(value >> 4)];
                    output[position++] = "0123456789ABCDEF"[(nuint)(value & (byte)15)];
                }
            }
            else output[position++] = (char)value;
        }
        output[position] = '"';
        written = required;
        return true;
    }
}
