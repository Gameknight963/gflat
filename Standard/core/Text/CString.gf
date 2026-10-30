namespace Standard::Text;

/// Helpers borrow byte buffers without owning, retaining or allocating their storage.
/// Unbounded inputs require a readable zero terminator; bounded operations use their capacities.
/// Operations use byte values without Unicode decoding or encoding validation.
public static class CString
{
    public static const nuint NotFound = (nuint)0 - (nuint)1;

    /// Returns true for null or an initial zero, reading at most the first byte of a nonnull input.
    public static bool IsNullOrEmpty(readonly(char)*? text) nothrow
    {
        if (text == null) return true;
        return text![0] == '\0';
    }

    /// Counts bytes before the first readable zero; the terminator is excluded.
    public static nuint Length(readonly(char)* text) nothrow
    {
        nuint length = 0;
        while (text[length] != '\0') length++;
        return length;
    }

    /// Searches logical bytes before the first zero; the zero itself never matches.
    /// Returns the first matching byte offset, or NotFound.
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

    /// Returns the last matching byte offset before the first zero, or NotFound; zero never matches.
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

    /// Scans at most capacity readable bytes for a zero, returning true and its byte offset.
    /// Returns false and sets length to zero when no terminator is found.
    /// Zero capacity does not read input; length is still set to zero.
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

    /// Copies bytes and their first zero when they fit; capacity includes the terminator.
    /// Failure leaves output unchanged; zero capacity accesses neither byte buffer.
    /// Successful copies allow overlap and leave bytes beyond the copied terminator unchanged.
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

    /// Appends terminated input to terminated output; capacity is the entire output extent.
    /// Failure leaves output unchanged; zero capacity accesses neither byte buffer.
    /// Overlap is allowed; an unterminated output returns false without reading input.
    public static bool TryAppend(readonly(char)* text, char* output, nuint capacity) nothrow
    {
        nuint length = 0;
        if (!TryGetLength(output, capacity, ref length)) return false;
        return TryCopy(text, output + length, capacity - length);
    }

    /// Compares unsigned bytes through the first zero, returning -1, zero or 1.
    /// Uses no locale, Unicode normalization or case folding.
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

    /// Tests equality of bytes before the first zero in each input.
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
