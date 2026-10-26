using std::Text;
namespace std::Json;

// Quotes/unquotes one counted UTF-8 JSON string, including surrounding quotes.
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

    public static bool TryGetUnquotedLength(readonly(char)* input, nuint count, ref nuint length) nothrow
    {
        length = 0;
        if (count < 2) return false;
        if (input[0] != '"' || input[count - 1] != '"') return false;
        nuint position = 1;
        nuint total = 0;
        while (position < count - 1)
        {
            uint scalar = 0u;
            if (!ReadQuotedScalar(input, count - 1, ref position, ref scalar)) return false;
            // Raw UTF-8 keeps its width; JSON escapes only shrink. The running
            // output count cannot exceed the already consumed input body.
            total += Utf8::EncodedLength(scalar);
        }
        length = total;
        return true;
    }

    // Disjoint or exact same-start buffers. Metadata refs must be disjoint.
    // Full validation/capacity preflight precedes every output store.
    public static bool TryUnquote(readonly(char)* input, nuint count,
        char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        nuint required = 0;
        if (!TryGetUnquotedLength(input, count, ref required) || capacity < required) return false;
        nuint position = 1;
        nuint target = 0;
        while (position < count - 1)
        {
            uint scalar = 0u;
            ReadQuotedScalar(input, count - 1, ref position, ref scalar);
            nuint size = 0;
            Utf8::TryEncode(scalar, output + target, capacity - target, &size);
            target += size;
        }
        written = required;
        return true;
    }

    // Compare decoded UTF-8 text without constructing it. False may short-circuit
    // before examining the rest of either input; this is not a validation API.
    public static bool ValueTextEquals(readonly(char)* input, nuint count,
        readonly(char)* text, nuint length) nothrow
    {
        if (count < 2) return false;
        if (input[0] != '"' || input[count - 1] != '"') return false;
        nuint position = 1;
        nuint compared = 0;
        while (position < count - 1)
        {
            uint scalar = 0u;
            if (!ReadQuotedScalar(input, count - 1, ref position, ref scalar)) return false;
            if (compared == length) return false;
            uint expected = 0u;
            nuint consumed = 0;
            if (!Utf8::TryDecode(text + compared, length - compared, &expected, &consumed)) return false;
            if (scalar != expected) return false;
            compared += consumed;
        }
        return compared == length;
    }

    // end excludes the closing quote. Consume the whole source scalar before
    // committing decoded bytes, permitting same-start shrinking in TryUnquote.
    private static bool ReadQuotedScalar(readonly(char)* input, nuint end,
        ref nuint position, ref uint scalar) nothrow
    {
        byte first = (byte)input[position];
        if (first < (byte)32 || first == (byte)'"') return false;
        if (first != (byte)'\\')
        {
            nuint consumed = 0;
            if (!Utf8::TryDecode(input + position, end - position, &scalar, &consumed)) return false;
            position += consumed;
            return true;
        }
        position++;
        if (position == end) return false;
        char escape = input[position++];
        if (escape == '"' || escape == '\\' || escape == '/') scalar = (uint)(byte)escape;
        else if (escape == 'b') scalar = 8u;
        else if (escape == 'f') scalar = 12u;
        else if (escape == 'n') scalar = 10u;
        else if (escape == 'r') scalar = 13u;
        else if (escape == 't') scalar = 9u;
        else if (escape == 'u')
        {
            if (end - position < 4 || !ReadHexQuad(input + position, ref scalar)) return false;
            position += 4;
            if (scalar >= 0xd800u && scalar <= 0xdbffu)
            {
                if (end - position < 6 || input[position] != '\\' || input[position + 1] != 'u') return false;
                uint low = 0u;
                if (!ReadHexQuad(input + position + 2, ref low) || low < 0xdc00u || low > 0xdfffu) return false;
                scalar = 0x10000u + ((scalar - 0xd800u) << 10) + low - 0xdc00u;
                position += 6;
            }
            else if (scalar >= 0xdc00u && scalar <= 0xdfffu) return false;
        }
        else return false;
        return true;
    }

    private static bool ReadHexQuad(readonly(char)* input, ref uint value) nothrow
    {
        uint decoded = 0u;
        for (nuint i = 0; i < 4; i++)
        {
            byte digit = (byte)input[i];
            uint next = 0u;
            if (digit >= (byte)'0' && digit <= (byte)'9') next = (uint)digit - 48u;
            else if (digit >= (byte)'a' && digit <= (byte)'f') next = (uint)digit - 97u + 10u;
            else if (digit >= (byte)'A' && digit <= (byte)'F') next = (uint)digit - 65u + 10u;
            else return false;
            decoded = (decoded << 4) | next;
        }
        value = decoded;
        return true;
    }
}
