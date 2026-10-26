using std::Text;
using std::Collections;
namespace std::Text;

// Owning counted conversions, separate from the allocation-free codecs.
public static class UnicodeText
{
    public static String FromUtf16(readonly(ushort)* input, nuint units)
    {
        nuint required = 0;
        if (!UnicodeTranscoding::TryGetUtf8Length(input, units, ref required))
            throw new Exception("Invalid UTF-16 or UTF-8 length overflow");
        String result = new();
        result.Reserve(required);
        nuint position = 0;
        char[4] chunk;
        while (position < units)
        {
            uint scalar = 0u; nuint consumed = 0; nuint written = 0;
            Utf16::TryDecode(input + position, units - position, &scalar, &consumed);
            Utf8::TryEncode(scalar, chunk, 4, &written);
            result.Append(chunk, written);
            position += consumed;
        }
        return move result;
    }

    public static Vector<ushort> ToUtf16(readonly(char)* input, nuint bytes)
    {
        nuint required = 0;
        if (!UnicodeTranscoding::TryGetUtf16Length(input, bytes, ref required))
            throw new Exception("Invalid UTF-8 text");
        Vector<ushort> result = new();
        result.Reserve(required);
        nuint position = 0;
        ushort[2] chunk;
        while (position < bytes)
        {
            uint scalar = 0u; nuint consumed = 0; nuint written = 0;
            Utf8::TryDecode(input + position, bytes - position, &scalar, &consumed);
            Utf16::TryEncode(scalar, chunk, 2, &written);
            for (nuint i = 0; i < written; i++) result.Add(chunk[i]);
            position += consumed;
        }
        return move result;
    }
}
