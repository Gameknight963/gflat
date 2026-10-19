namespace std;

// Borrowed null-terminated byte strings; no encoding or ownership is implied.
public static class CString
{
    public static nuint Length(readonly(char)* text) nothrow
    {
        nuint length = 0;
        while (text[length] != '\0') length++;
        return length;
    }

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
