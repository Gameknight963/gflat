namespace std;

// Canonical uppercase RFC 4648 Base32, with required '=' padding.
// Counted results have no terminator. Encoding requires disjoint buffers.
public static class Base32
{
    public static bool TryGetEncodedLength(nuint count, ref nuint length) nothrow
    {
        length = 0;
        nuint maximum = (nuint)0 - (nuint)1;
        nuint groups = count / 5;
        if (groups > maximum / 8) return false;
        nuint required = groups * 8;
        if (count % 5 != 0)
        {
            if (required > maximum - 8) return false;
            required += 8;
        }
        length = required;
        return true;
    }

    // Failure leaves output untouched and written=0. Metadata is disjoint.
    public static bool TryEncode(immutable(char*) data, nuint count,
        char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        nuint required = 0;
        if (!TryGetEncodedLength(count, ref required) || required > capacity) return false;
        immutable(char*) alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        uint pending = 0u;
        int bits = 0;
        nuint target = 0;
        for (nuint i = 0; i < count; i++)
        {
            // At most four retained bits plus eight input bits fit in uint.
            pending = (pending << 8) | (uint)(byte)data[i];
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                output[target++] = alphabet[(nuint)((pending >> bits) & 31u)];
            }
            pending = pending & ((1u << bits) - 1u);
        }
        if (bits != 0) output[target++] = alphabet[(nuint)(pending << (5 - bits))];
        while (target < required) output[target++] = '=';
        written = required;
        return true;
    }

    // Validate the entire alphabet, padding shape, and unused low bits.
    public static bool TryGetDecodedLength(immutable(char*) text, nuint count,
        ref nuint length) nothrow
    {
        length = 0;
        if (count % 8 != 0) return false;
        if (count == 0) return true;
        nuint padding = 0;
        while (padding < 6 && text[count - 1 - padding] == '=') padding++;
        nuint removed = 0;
        int unused = 0;
        if (padding == 1) { removed = 1; unused = 3; }
        else if (padding == 3) { removed = 2; unused = 1; }
        else if (padding == 4) { removed = 3; unused = 4; }
        else if (padding == 6) { removed = 4; unused = 2; }
        else if (padding != 0) return false;
        nuint digits = count - padding;
        int last = 0;
        for (nuint i = 0; i < digits; i++)
        {
            last = Digit(text[i]);
            if (last < 0) return false;
        }
        if ((last & ((1 << unused) - 1)) != 0) return false;
        // Divide first: floor(maximum/8)*5 cannot overflow nuint.
        length = (count / 8) * 5 - removed;
        return true;
    }

    // Disjoint or exact same-start buffers. Validate everything before writing;
    // the shrinking accumulator consumes each source digit before any overlap.
    public static bool TryDecode(immutable(char*) text, nuint count,
        char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        nuint required = 0;
        if (!TryGetDecodedLength(text, count, ref required) || required > capacity) return false;
        uint pending = 0u;
        int bits = 0;
        nuint target = 0;
        for (nuint i = 0; i < count && text[i] != '='; i++)
        {
            // At most seven retained bits plus five input bits fit in uint.
            pending = (pending << 5) | (uint)Digit(text[i]);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output[target++] = (char)(pending >> bits);
                pending = pending & ((1u << bits) - 1u);
            }
        }
        written = required;
        return true;
    }

    private static int Digit(char value) nothrow
    {
        if (value >= 'A' && value <= 'Z') return (int)value - 65;
        if (value >= '2' && value <= '7') return (int)value - 50 + 26;
        return -1;
    }
}
