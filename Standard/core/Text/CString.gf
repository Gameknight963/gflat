namespace Standard::Text;

// Byte operations for borrowed zero-terminated strings and bounded buffers.
public static class CString
{
    public static const nuint NotFound = (nuint)0 - (nuint)1;

    // Returns true for null or an initial zero byte.
    public static bool IsNullOrEmpty(readonly(char)*? text) nothrow
    {
        if (text == null) return true;
        return text![0] == '\0';
    }

    // Counts bytes before the first readable zero. The terminator is excluded.
    public static nuint Length(readonly(char)* text) nothrow
    {
        nuint length = 0;
        while (text[length] != '\0') length++;
        return length;
    }

    // Returns the first matching byte offset, or NotFound. The terminating zero never matches.
    public static nuint IndexOf(readonly(char)* text, char value) nothrow
    {
        nuint index = 0;
        while (text[index] != '\0')
        {
            if (text[index] == value) return index;
            index++;
        }
        return NotFound;
    }

    // Returns the last matching byte offset, or NotFound. The terminating zero never matches.
    public static nuint LastIndexOf(readonly(char)* text, char value) nothrow
    {
        nuint result = NotFound;
        nuint index = 0;
        while (text[index] != '\0')
        {
            if (text[index] == value) result = index;
            index++;
        }
        return result;
    }

    // Finds a zero within capacity bytes and returns its offset through length.
    // A missing terminator returns false and sets length to zero. Zero capacity reads no bytes.
    public static bool TryGetLength(readonly(char)* text, nuint capacity, ref nuint length) nothrow
    {
        for (nuint i = 0; i < capacity; i++)
        {
            if (text[i] == '\0')
            {
                length = i;
                return true;
            }
        }
        length = 0;
        return false;
    }

    // Copies bytes and their terminator when capacity allows. Overlap is supported.
    // Failure preserves output. Zero capacity accesses neither buffer.
    public static bool TryCopy(readonly(char)* text, char* output, nuint capacity) nothrow
    {
        nuint length = 0;
        if (!TryGetLength(text, capacity, ref length)) return false;
        nuint count = length + 1;
        if ((readonly(char)*)output > text && (readonly(char)*)output < text + count)
        {
            nuint i = count;
            while (i != 0) { i--; output[i] = text[i]; }
        }
        else
            for (nuint i = 0; i < count; i++) output[i] = text[i];
        return true;
    }

    // Appends a C string within the output buffer's total capacity. Overlap is supported.
    // Failure preserves output. An unterminated destination returns false before reading input.
    // Zero capacity accesses neither byte buffer.
    public static bool TryAppend(readonly(char)* text, char* output, nuint capacity) nothrow
    {
        nuint length = 0;
        if (!TryGetLength(output, capacity, ref length)) return false;
        return TryCopy(text, output + length, capacity - length);
    }

    // Compares unsigned bytes through the first zero, returning -1, zero, or 1.
    public static int Compare(readonly(char)* left, readonly(char)* right) nothrow
    {
        nuint index = 0;
        while (true)
        {
            byte first = (byte)left[index];
            byte second = (byte)right[index];
            if (first < second) return -1;
            if (first > second) return 1;
            if (first == (byte)0) return 0;
            index++;
        }
    }

    // Tests equality of bytes before the first zero in each input.
    public static bool Equals(readonly(char)* left, readonly(char)* right) nothrow
        => Compare(left, right) == 0;

    public static bool StartsWith(readonly(char)* text, readonly(char)* prefix) nothrow
    {
        nuint index = 0;
        while (prefix[index] != '\0')
        {
            if (text[index] != prefix[index]) return false;
            index++;
        }
        return true;
    }

    public static bool EndsWith(readonly(char)* text, readonly(char)* suffix) nothrow
    {
        nuint length = Length(text);
        nuint count = Length(suffix);
        if (count > length) return false;
        return StartsWith(text + length - count, suffix);
    }
}
