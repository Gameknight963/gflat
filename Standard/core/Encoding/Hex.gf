namespace Standard::Encoding;

// Counted hexadecimal bytes. No separators, whitespace, prefix, or terminator.
public static class Hex
{
    public static bool TryGetEncodedLength(nuint count, ref nuint length) nothrow
    {
        length = 0;
        if (count > ((nuint)0 - 1) / 2) return false;
        length = count * 2;
        return true;
    }

    // Input and output must not overlap. Failure leaves output untouched.
    public static bool TryEncode(immutable(char*) data, nuint count, char* output, nuint capacity, ref nuint written, bool uppercase = true) nothrow
    {
        written = 0;
        nuint required = 0;
        if (!Hex::TryGetEncodedLength(count, ref required) || required > capacity) return false;
        readonly(char)* digits = "0123456789ABCDEF";
        if (!uppercase) digits = "0123456789abcdef";
        for (nuint i = 0; i < count; i++)
        {
            int value = (int)(byte)data[i];
            output[i * 2] = digits[value >> 4];
            output[i * 2 + 1] = digits[value & 15];
        }
        written = required;
        return true;
    }

    public static bool TryGetDecodedLength(immutable(char*) text, nuint count, ref nuint length) nothrow
    {
        length = 0;
        if (count % 2 != 0) return false;
        for (nuint i = 0; i < count; i++) if (Hex::Digit(text[i]) < 0) return false;
        length = count / 2;
        return true;
    }

    // Accepts either letter case. Supports disjoint buffers or exact same start.
    // All input is validated first; failure leaves output untouched and written=0.
    public static bool TryDecode(immutable(char*) text, nuint count, char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        nuint required = 0;
        if (!Hex::TryGetDecodedLength(text, count, ref required) || required > capacity) return false;
        for (nuint i = 0; i < required; i++)
        {
            int high = Hex::Digit(text[i * 2]);
            int low = Hex::Digit(text[i * 2 + 1]);
            output[i] = (char)((high << 4) | low);
        }
        written = required;
        return true;
    }

    private static int Digit(char value) nothrow
    {
        if (value >= '0' && value <= '9') return (int)value - 48;
        if (value >= 'A' && value <= 'F') return (int)value - 65 + 10;
        if (value >= 'a' && value <= 'f') return (int)value - 97 + 10;
        return -1;
    }
}
