namespace std::Json;

// Optional writer adapter. Unlike JsonString's counted-buffer API, writer
// failures can leave a prefix in the destination after preflight succeeds.
public void WriteJsonString(this ITextWriter* output, readonly(char)* input, nuint length)
{
    nuint required = 0;
    if (!JsonString::TryGetQuotedLength(input, length, ref required))
        throw new Exception("Invalid UTF-8 or length overflow in JSON string");
    output.Write("\"", 1);
    nuint start = 0;
    char[8] escaped;
    for (nuint i = 0; i < length; i++)
    {
        byte value = (byte)input[i];
        if (value < (byte)32 || value == (byte)'"' || value == (byte)'\\')
        {
            if (i > start) output.Write(input + start, i - start);
            nuint written = 0;
            // This is one ASCII byte and eight bytes cover even a \u00XX escape
            // plus its quotes. Reuse the core encoder's escape policy.
            if (!JsonString::TryQuote(input + i, 1, escaped, 8, ref written))
                throw new Exception("JSON string escape failed");
            output.Write(&escaped[1], written - 2);
            start = i + 1;
        }
    }
    if (start < length) output.Write(input + start, length - start);
    output.Write("\"", 1);
}
