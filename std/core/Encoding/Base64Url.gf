namespace std::Encoding;

// RFC 4648 URL-safe alphabet, strictly unpadded. Counted binary/ASCII buffers.
public static class Base64Url
{
    public static bool TryGetEncodedLength(nuint count, ref nuint length) nothrow
    {
        length = 0;
        nuint maximum = (nuint)0 - 1;
        nuint groups = count / 3;
        if (groups > maximum / 4) return false;
        nuint required = groups * 4;
        nuint remainder = count % 3;
        if (remainder != 0)
        {
            nuint tail = remainder + 1;
            if (required > maximum - tail) return false;
            required += tail;
        }
        length = required;
        return true;
    }

    // Encoding requires disjoint buffers. No terminator is written.
    public static bool TryEncode(readonly(char)* data, nuint count, char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        nuint required = 0;
        if (!Base64Url::TryGetEncodedLength(count, ref required) || required > capacity) return false;
        readonly(char)* alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
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
            if (count - index == 2) output[target + 2] = alphabet[(b & 15) << 2];
        }
        written = required;
        return true;
    }

    // Validate the entire alphabet and the unused low bits of a final short group.
    public static bool TryGetDecodedLength(readonly(char)* text, nuint count, ref nuint length) nothrow
    {
        length = 0;
        nuint remainder = count % 4;
        if (remainder == 1) return false;
        for (nuint i = 0; i < count; i++)
            if (Base64Url::Digit(text[i]) < 0) return false;
        if (remainder == 2 && (Base64Url::Digit(text[count - 1]) & 15) != 0) return false;
        if (remainder == 3 && (Base64Url::Digit(text[count - 1]) & 3) != 0) return false;
        length = (count / 4) * 3;
        if (remainder != 0) length += remainder - 1;
        return true;
    }

    // Disjoint buffers or exact same start address. Preflight precedes every write.
    public static bool TryDecode(readonly(char)* text, nuint count, char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        nuint required = 0;
        if (!Base64Url::TryGetDecodedLength(text, count, ref required) || required > capacity) return false;
        nuint index = 0;
        nuint target = 0;
        while (count - index >= 4)
        {
            int a = Base64Url::Digit(text[index]);
            int b = Base64Url::Digit(text[index + 1]);
            int c = Base64Url::Digit(text[index + 2]);
            int d = Base64Url::Digit(text[index + 3]);
            output[target] = (char)((a << 2) | (b >> 4));
            output[target + 1] = (char)((b << 4) | (c >> 2));
            output[target + 2] = (char)((c << 6) | d);
            index += 4; target += 3;
        }
        if (index < count)
        {
            int a = Base64Url::Digit(text[index]);
            int b = Base64Url::Digit(text[index + 1]);
            int c = 0;
            if (count - index == 3) c = Base64Url::Digit(text[index + 2]);
            output[target] = (char)((a << 2) | (b >> 4));
            if (count - index == 3) output[target + 1] = (char)((b << 4) | (c >> 2));
        }
        written = required;
        return true;
    }

    private static int Digit(char value) nothrow
    {
        if (value >= 'A' && value <= 'Z') return (int)value - 65;
        if (value >= 'a' && value <= 'z') return (int)value - 97 + 26;
        if (value >= '0' && value <= '9') return (int)value - 48 + 52;
        if (value == '-') return 62;
        if (value == '_') return 63;
        return -1;
    }
}
