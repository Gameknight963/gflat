using Standard::Text;
using Standard::Collections;

namespace Standard::Text;

// Owning results of Unicode text operations.
public static class UnicodeText
{
    public static String Trim(readonly(char)* input, nuint bytes)
        => UnicodeText::TrimCore(input, bytes, true, true);
    public static String TrimStart(readonly(char)* input, nuint bytes)
        => UnicodeText::TrimCore(input, bytes, true, false);
    public static String TrimEnd(readonly(char)* input, nuint bytes)
        => UnicodeText::TrimCore(input, bytes, false, true);

    public static String Trim(readonly(char)* input) => UnicodeText::Trim(input, CString::Length(input));
    public static String TrimStart(readonly(char)* input) => UnicodeText::TrimStart(input, CString::Length(input));
    public static String TrimEnd(readonly(char)* input) => UnicodeText::TrimEnd(input, CString::Length(input));

    private static String TrimCore(readonly(char)* input, nuint bytes, bool trimStart, bool trimEnd)
    {
        if (!Utf8::IsValid(input, bytes)) throw new Exception("Invalid UTF-8 text");
        nuint start = 0;
        nuint end = bytes;
        if (trimStart)
        {
            while (start < end)
            {
                uint scalar = 0u;
                nuint consumed = 0;
                Utf8::TryDecode(input + start, end - start, &scalar, &consumed);
                if (!Rune::IsWhiteSpace(new Rune(scalar))) break;
                start += consumed;
            }
        }
        if (trimEnd)
        {
            while (end > start)
            {
                uint scalar = 0u;
                nuint consumed = 0;
                Utf8::TryDecodeLast(input + start, end - start, &scalar, &consumed);
                if (!Rune::IsWhiteSpace(new Rune(scalar))) break;
                end -= consumed;
            }
        }
        return String::From(input + start, end - start);
    }

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
