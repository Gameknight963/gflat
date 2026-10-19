namespace std;

// Canonical RFC 4648 Base64, using '+'/'/' and required padding.
// Counted buffers include embedded zeros; output is not null-terminated.
public static class Base64
{
    public static bool TryGetEncodedLength(nuint count, ref nuint length)
    {
        length = 0;
        nuint maximum = (nuint)0 - 1;
        nuint groups = count / 3;
        if (groups > maximum / 4) return false;
        nuint required = groups * 4;
        if (count % 3 != 0)
        {
            if (required > maximum - 4) return false;
            required += 4;
        }
        length = required;
        return true;
    }

    // Input/output must not overlap. Failure leaves output untouched and written=0.
    public static bool TryEncode(readonly(char*) data, nuint count, char* output, nuint capacity, ref nuint written)
    {
        written = 0;
        nuint required = 0;
        if (!Base64::TryGetEncodedLength(count, ref required) || required > capacity) return false;
        readonly(char*) alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        nuint index = 0;
        nuint target = 0;
        while (count - index >= 3)
        {
            int a = (int)(byte)data[index];
            int b = (int)(byte)data[index + 1];
            int c = (int)(byte)data[index + 2];
            output[target] = alphabet[a >> 2];
            output[target + 1] = alphabet[((a & 3) << 4) | (b >> 4)];
            output[target + 2] = alphabet[((b & 15) << 2) | (c >> 6)];
            output[target + 3] = alphabet[c & 63];
            index += 3; target += 4;
        }
        if (index < count)
        {
            int a = (int)(byte)data[index];
            int b = 0;
            if (count - index == 2) b = (int)(byte)data[index + 1];
            output[target] = alphabet[a >> 2];
            output[target + 1] = alphabet[((a & 3) << 4) | (b >> 4)];
            output[target + 2] = '=';
            if (count - index == 2) output[target + 2] = alphabet[(b & 15) << 2];
            output[target + 3] = '=';
        }
        written = required;
        return true;
    }

    // Validates every byte, including canonical zero padding bits.
    public static bool TryGetDecodedLength(readonly(char*) text, nuint count, ref nuint length)
    {
        length = 0;
        if (count % 4 != 0) return false;
        if (count == 0) return true;
        nuint padding = 0;
        if (text[count - 1] == '=') padding++;
        if (text[count - 2] == '=') padding++;
        for (nuint i = 0; i < count; i += 4)
        {
            int a = Base64::Digit(text[i]);
            int b = Base64::Digit(text[i + 1]);
            int c = Base64::Digit(text[i + 2]);
            int d = Base64::Digit(text[i + 3]);
            if (a < 0 || b < 0) return false;
            if (text[i + 2] == '=')
            {
                if (i != count - 4 || text[i + 3] != '=' || (b & 15) != 0) return false;
            }
            else
            {
                if (c < 0) return false;
                if (text[i + 3] == '=')
                {
                    if (i != count - 4 || (c & 3) != 0) return false;
                }
                else if (d < 0) return false;
            }
        }
        length = (count / 4) * 3 - padding;
        return true;
    }

    // Disjoint buffers or exact same start address are supported. Input is
    // validated before writing, so malformed input/capacity failure changes nothing.
    public static bool TryDecode(readonly(char*) text, nuint count, char* output, nuint capacity, ref nuint written)
    {
        written = 0;
        nuint required = 0;
        if (!Base64::TryGetDecodedLength(text, count, ref required) || required > capacity) return false;
        nuint target = 0;
        for (nuint i = 0; i < count; i += 4)
        {
            int a = Base64::Digit(text[i]);
            int b = Base64::Digit(text[i + 1]);
            int c = 0;
            int d = 0;
            if (text[i + 2] != '=') c = Base64::Digit(text[i + 2]);
            if (text[i + 3] != '=') d = Base64::Digit(text[i + 3]);
            output[target] = (char)((a << 2) | (b >> 4)); target++;
            if (target < required) { output[target] = (char)((b << 4) | (c >> 2)); target++; }
            if (target < required) { output[target] = (char)((c << 6) | d); target++; }
        }
        written = required;
        return true;
    }

    private static int Digit(char value)
    {
        if (value >= 'A' && value <= 'Z') return (int)value - 65;
        if (value >= 'a' && value <= 'z') return (int)value - 97 + 26;
        if (value >= '0' && value <= '9') return (int)value - 48 + 52;
        if (value == '+') return 62;
        if (value == '/') return 63;
        return -1;
    }
}
