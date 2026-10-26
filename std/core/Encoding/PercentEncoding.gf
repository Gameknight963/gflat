namespace std;

// RFC 3986 component data, not a URI parser or form-urlencoded codec.
// Counted bytes, no allocation, and no trailing null in output.
public static class PercentEncoding
{
    public static bool TryGetEncodedLength(immutable(char*) data, nuint count, ref nuint length) nothrow
    {
        length = 0;
        nuint required = 0;
        nuint maximum = (nuint)0 - 1;
        for (nuint i = 0; i < count; i++)
        {
            nuint amount = 1;
            if (!PercentEncoding::IsUnreserved(data[i])) amount = 3;
            if (amount > maximum - required) return false;
            required += amount;
        }
        length = required;
        return true;
    }

    // Input/output must be disjoint. Failure changes no output bytes.
    public static bool TryEncode(immutable(char*) data, nuint count, char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        if (count > capacity) return false;
        nuint required = 0;
        if (!PercentEncoding::TryGetEncodedLength(data, count, ref required) || required > capacity) return false;
        immutable(char*) digits = "0123456789ABCDEF";
        nuint target = 0;
        for (nuint i = 0; i < count; i++)
        {
            if (PercentEncoding::IsUnreserved(data[i])) { output[target] = data[i]; target++; }
            else
            {
                int value = (int)(byte)data[i];
                output[target] = '%';
                output[target + 1] = digits[value >> 4];
                output[target + 2] = digits[value & 15];
                target += 3;
            }
        }
        written = required;
        return true;
    }

    public static bool TryGetDecodedLength(immutable(char*) text, nuint count, ref nuint length) nothrow
    {
        length = 0;
        nuint index = 0;
        nuint required = 0;
        while (index < count)
        {
            if (text[index] == '%')
            {
                if (count - index < 3 || PercentEncoding::Digit(text[index + 1]) < 0 || PercentEncoding::Digit(text[index + 2]) < 0) return false;
                index += 3;
            }
            else index++;
            required++;
        }
        length = required;
        return true;
    }

    // Decodes once. '+' remains '+'. Disjoint or exact same-address buffers
    // are supported. Malformed input/capacity failure leaves output untouched.
    public static bool TryDecode(immutable(char*) text, nuint count, char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        nuint required = 0;
        if (!PercentEncoding::TryGetDecodedLength(text, count, ref required) || required > capacity) return false;
        nuint index = 0;
        nuint target = 0;
        while (index < count)
        {
            if (text[index] == '%')
            {
                int high = PercentEncoding::Digit(text[index + 1]);
                int low = PercentEncoding::Digit(text[index + 2]);
                output[target] = (char)((high << 4) | low);
                index += 3;
            }
            else { output[target] = text[index]; index++; }
            target++;
        }
        written = required;
        return true;
    }

    private static bool IsUnreserved(char value) nothrow
        => (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z') ||
           (value >= '0' && value <= '9') || value == '-' || value == '.' || value == '_' || value == '~';

    private static int Digit(char value) nothrow
    {
        if (value >= '0' && value <= '9') return (int)value - 48;
        if (value >= 'A' && value <= 'F') return (int)value - 65 + 10;
        if (value >= 'a' && value <= 'f') return (int)value - 97 + 10;
        return -1;
    }
}
