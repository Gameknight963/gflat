namespace std;

// Optional whole-buffer conversions. Source, destination, and result metadata
// must be disjoint. Lengths count UTF-8 bytes or UTF-16 ushort code units.
public static class UnicodeTranscoding
{
    public static bool TryGetUtf16Length(readonly(char)* input, nuint bytes, ref nuint units) nothrow
    {
        nuint position = 0;
        nuint required = 0;
        while (position < bytes)
        {
            uint scalar = 0u;
            nuint consumed = 0;
            if (!std::Utf8::TryDecode(input + position, bytes - position, &scalar, &consumed))
            { units = 0; return false; }
            // A decoded scalar uses no more UTF-16 units than its UTF-8 bytes.
            required += std::Utf16::EncodedLength(scalar);
            position += consumed;
        }
        units = required;
        return true;
    }

    public static bool TryGetUtf8Length(readonly(ushort)* input, nuint units, ref nuint bytes) nothrow
    {
        nuint position = 0;
        nuint required = 0;
        nuint maximum = (nuint)0 - (nuint)1;
        while (position < units)
        {
            uint scalar = 0u;
            nuint consumed = 0;
            if (!std::Utf16::TryDecode(input + position, units - position, &scalar, &consumed))
            { bytes = 0; return false; }
            nuint encoded = std::Utf8::EncodedLength(scalar);
            if (encoded > maximum - required) { bytes = 0; return false; }
            required += encoded;
            position += consumed;
        }
        bytes = required;
        return true;
    }

    // Validate the entire source and capacity before the first output write.
    public static bool TryUtf8ToUtf16(readonly(char)* input, nuint bytes,
        ushort* output, nuint capacity, ref nuint written) nothrow
    {
        nuint required = 0;
        if (!UnicodeTranscoding::TryGetUtf16Length(input, bytes, ref required) || required > capacity)
        { written = 0; return false; }
        nuint position = 0;
        nuint produced = 0;
        while (position < bytes)
        {
            uint scalar = 0u;
            nuint consumed = 0;
            nuint encoded = 0;
            std::Utf8::TryDecode(input + position, bytes - position, &scalar, &consumed);
            std::Utf16::TryEncode(scalar, output + produced, capacity - produced, &encoded);
            position += consumed;
            produced += encoded;
        }
        written = produced;
        return true;
    }

    public static bool TryUtf16ToUtf8(readonly(ushort)* input, nuint units,
        char* output, nuint capacity, ref nuint written) nothrow
    {
        nuint required = 0;
        if (!UnicodeTranscoding::TryGetUtf8Length(input, units, ref required) || required > capacity)
        { written = 0; return false; }
        nuint position = 0;
        nuint produced = 0;
        while (position < units)
        {
            uint scalar = 0u;
            nuint consumed = 0;
            nuint encoded = 0;
            std::Utf16::TryDecode(input + position, units - position, &scalar, &consumed);
            std::Utf8::TryEncode(scalar, output + produced, capacity - produced, &encoded);
            position += consumed;
            produced += encoded;
        }
        written = produced;
        return true;
    }
}
