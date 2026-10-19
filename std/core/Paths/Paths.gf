namespace std;

// Lexical paths, independent of the host OS. No filesystem access or normalization.
public static class Paths
{
    public static bool IsSeparator(char value, PathStyle style)
        => value == '/' || (style == PathStyle::Windows && value == '\\');

    public static String GetFileName(readonly(char*) path, PathStyle style)
    {
        nuint length = Paths::Length(path);
        nuint start = Paths::FileNameStart(path, length, style);
        return String::From(path + start, length - start);
    }

    public static String GetDirectoryName(readonly(char*) path, PathStyle style)
    {
        nuint length = Paths::Length(path);
        nuint root = Paths::RootLength(path, length, style);
        if (length <= root) return new String();
        nuint end = length;
        while (end > root && !Paths::IsSeparator(path[end - 1], style)) end--;
        if (end > root) end--;
        while (end > root && Paths::IsSeparator(path[end - 1], style)) end--;
        return String::From(path, end);
    }

    public static String GetExtension(readonly(char*) path, PathStyle style)
    {
        nuint length = Paths::Length(path);
        nuint start = Paths::FileNameStart(path, length, style);
        nuint dot = Paths::ExtensionStart(path, start, length);
        if (dot == length || dot + 1 == length) return new String();
        return String::From(path + dot, length - dot);
    }

    public static String GetFileNameWithoutExtension(readonly(char*) path, PathStyle style)
    {
        nuint length = Paths::Length(path);
        nuint start = Paths::FileNameStart(path, length, style);
        nuint dot = Paths::ExtensionStart(path, start, length);
        return String::From(path + start, dot - start);
    }

    public static String GetRoot(readonly(char*) path, PathStyle style)
    {
        return String::From(path, Paths::RootLength(path, Paths::Length(path), style));
    }

    // Concatenates components, even if right is rooted. Removes one duplicated
    // separator at the join boundary; otherwise preserves the original spelling.
    // Does not resolve dot segments, validate names, or change existing separators.
    public static String Join(readonly(char*) left, readonly(char*) right, PathStyle style)
    {
        nuint leftLength = Paths::Length(left);
        nuint rightLength = Paths::Length(right);
        if (leftLength == 0) return String::From(right, rightLength);
        String result = String::From(left, leftLength);
        if (rightLength == 0) return move result;
        bool leftSeparator = Paths::IsSeparator(left[leftLength - 1], style);
        bool rightSeparator = Paths::IsSeparator(right[0], style);
        nuint start = 0;
        if (leftSeparator && rightSeparator) start = 1;
        if (!leftSeparator && !rightSeparator)
        {
            if (style == PathStyle::Windows) result.AppendLiteral("\\", 1);
            else result.AppendLiteral("/", 1);
        }
        result.AppendLiteral(right + start, rightLength - start);
        return move result;
    }

    private static nuint Length(readonly(char*) text)
    {
        nuint length = 0;
        while (text[length] != '\0') length++;
        return length;
    }

    private static nuint FileNameStart(readonly(char*) path, nuint length, PathStyle style)
    {
        nuint root = Paths::RootLength(path, length, style);
        nuint start = length;
        while (start > root && !Paths::IsSeparator(path[start - 1], style)) start--;
        return start;
    }

    private static nuint ExtensionStart(readonly(char*) path, nuint start, nuint end)
    {
        nuint index = end;
        while (index > start)
        {
            index--;
            if (path[index] == '.') return index;
        }
        return end;
    }

    private static nuint RootLength(readonly(char*) path, nuint length, PathStyle style)
    {
        if (length == 0) return 0;
        if (style == PathStyle::Unix)
        {
            if (path[0] == '/') return 1;
            return 0;
        }
        bool letter = (path[0] >= 'a' && path[0] <= 'z') || (path[0] >= 'A' && path[0] <= 'Z');
        if (length >= 2 && letter && path[1] == ':')
        {
            if (length >= 3 && Paths::IsSeparator(path[2], style)) return 3;
            return 2;
        }
        if (!Paths::IsSeparator(path[0], style)) return 0;
        if (length < 2 || !Paths::IsSeparator(path[1], style)) return 1;
        // UNC server/share root, including its following separator when present.
        nuint index = 2;
        while (index < length && !Paths::IsSeparator(path[index], style)) index++;
        if (index == length) return length;
        index++;
        while (index < length && !Paths::IsSeparator(path[index], style)) index++;
        if (index < length) index++;
        return index;
    }
}
