using std::Text;
using std::Memory;
namespace std::IO;

// Lexical paths, independent of the host OS. No filesystem access or normalization.
public static class Paths
{
    public static bool IsSeparator(char value, PathStyle style) nothrow
        => value == '/' || (style == PathStyle::Windows && value == '\\');

    public static bool EndsInDirectorySeparator(immutable(char*) path, PathStyle style) nothrow
    {
        nuint length = Paths::Length(path);
        return length != 0 && Paths::IsSeparator(path[length - 1], style);
    }

    public static bool EndsInDirectorySeparator(Span<readonly(char)> path, PathStyle style) nothrow
        => !path.IsEmpty && Paths::IsSeparator(path.Data![path.Length - 1], style);

    // Remove one separator only, preserving roots under the existing lexical rules.
    public static String TrimEndingDirectorySeparator(immutable(char*) path, PathStyle style)
    {
        nuint length = Paths::Length(path);
        if (length != 0 && Paths::IsSeparator(path[length - 1], style)
            && length > Paths::RootLength(path, length, style)) length--;
        return String::From(path, length);
    }

    public static Span<readonly(char)> TrimEndingDirectorySeparator(Span<readonly(char)> path, PathStyle style)
    {
        if (path.IsEmpty) return new Span<readonly(char)>();
        nuint length = path.Length;
        if (Paths::IsSeparator(path.Data![length - 1], style)
            && length > Paths::RootLength(path.Data!, length, style)) length--;
        return path.Slice(0, length);
    }

    // A rooted Windows path can still depend on the current drive or its directory.
    public static bool IsRooted(immutable(char*) path, PathStyle style) nothrow
        => Paths::IsRootedCore(path, Paths::PrefixLength(path, 2), style);

    // Classifies the root spelling only; this does not validate a UNC share or name.
    public static bool IsFullyQualified(immutable(char*) path, PathStyle style) nothrow
        => Paths::IsFullyQualifiedCore(path, Paths::PrefixLength(path, 3), style);

    public static bool IsRooted(Span<readonly(char)> path, PathStyle style) nothrow
    {
        if (path.IsEmpty) return false;
        return Paths::IsRootedCore(path.Data!, path.Length, style);
    }

    public static bool IsFullyQualified(Span<readonly(char)> path, PathStyle style) nothrow
    {
        if (path.IsEmpty) return false;
        return Paths::IsFullyQualifiedCore(path.Data!, path.Length, style);
    }

    public static String GetFileName(immutable(char*) path, PathStyle style)
    {
        nuint length = Paths::Length(path);
        nuint start = Paths::FileNameStart(path, length, style);
        return String::From(path + start, length - start);
    }

    public static String GetDirectoryName(immutable(char*) path, PathStyle style)
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

    public static String GetExtension(immutable(char*) path, PathStyle style)
    {
        nuint length = Paths::Length(path);
        nuint start = Paths::FileNameStart(path, length, style);
        nuint dot = Paths::ExtensionStart(path, start, length);
        if (dot == length || dot + 1 == length) return new String();
        return String::From(path + dot, length - dot);
    }

    public static String GetFileNameWithoutExtension(immutable(char*) path, PathStyle style)
    {
        nuint length = Paths::Length(path);
        nuint start = Paths::FileNameStart(path, length, style);
        nuint dot = Paths::ExtensionStart(path, start, length);
        return String::From(path + start, dot - start);
    }

    public static String GetRoot(immutable(char*) path, PathStyle style)
    {
        return String::From(path, Paths::RootLength(path, Paths::Length(path), style));
    }

    // Borrowed counted views; no terminator is read or appended.
    public static Span<readonly(char)> GetFileName(Span<readonly(char)> path, PathStyle style)
    {
        if (path.IsEmpty) return new Span<readonly(char)>();
        nuint start = Paths::FileNameStart(path.Data!, path.Length, style);
        return path.Slice(start);
    }

    public static Span<readonly(char)> GetDirectoryName(Span<readonly(char)> path, PathStyle style)
    {
        if (path.IsEmpty) return new Span<readonly(char)>();
        nuint root = Paths::RootLength(path.Data!, path.Length, style);
        if (path.Length <= root) return new Span<readonly(char)>();
        nuint end = path.Length;
        while (end > root && !Paths::IsSeparator(path[end - 1], style)) end--;
        if (end > root) end--;
        while (end > root && Paths::IsSeparator(path[end - 1], style)) end--;
        return path.Slice(0, end);
    }

    public static Span<readonly(char)> GetExtension(Span<readonly(char)> path, PathStyle style)
    {
        if (path.IsEmpty) return new Span<readonly(char)>();
        nuint start = Paths::FileNameStart(path.Data!, path.Length, style);
        nuint dot = Paths::ExtensionStart(path.Data!, start, path.Length);
        if (dot == path.Length || dot + 1 == path.Length) return new Span<readonly(char)>();
        return path.Slice(dot);
    }

    public static Span<readonly(char)> GetFileNameWithoutExtension(Span<readonly(char)> path, PathStyle style)
    {
        if (path.IsEmpty) return new Span<readonly(char)>();
        nuint start = Paths::FileNameStart(path.Data!, path.Length, style);
        nuint dot = Paths::ExtensionStart(path.Data!, start, path.Length);
        return path.Slice(start, dot - start);
    }

    public static Span<readonly(char)> GetRoot(Span<readonly(char)> path, PathStyle style)
    {
        if (path.IsEmpty) return new Span<readonly(char)>();
        return path.Slice(0, Paths::RootLength(path.Data!, path.Length, style));
    }

    // Concatenates components, even if right is rooted. Removes one duplicated
    // separator at the join boundary; otherwise preserves the original spelling.
    // Does not resolve dot segments, validate names, or change existing separators.
    public static String Join(immutable(char*) left, immutable(char*) right, PathStyle style)
    {
        return Paths::JoinCore(left, Paths::Length(left), right, Paths::Length(right), style);
    }

    public static String Join(Span<readonly(char)> left, Span<readonly(char)> right, PathStyle style)
    {
        readonly(char)* leftData = "";
        readonly(char)* rightData = "";
        if (!left.IsEmpty) leftData = left.Data!;
        if (!right.IsEmpty) rightData = right.Data!;
        return Paths::JoinCore(leftData, left.Length, rightData, right.Length, style);
    }

    // Inspect only the root prefix of a C string, preserving constant-time checks.
    private static nuint PrefixLength(immutable(char*) path, nuint limit) nothrow
    {
        nuint length = 0;
        while (length < limit && path[length] != '\0') length++;
        return length;
    }

    private static bool IsRootedCore(immutable(char*) path, nuint count, PathStyle style) nothrow
    {
        if (count == 0) return false;
        if (Paths::IsSeparator(path[0], style)) return true;
        if (style == PathStyle::Unix || count < 2) return false;
        bool letter = (path[0] >= 'a' && path[0] <= 'z') || (path[0] >= 'A' && path[0] <= 'Z');
        return letter && path[1] == ':';
    }

    private static bool IsFullyQualifiedCore(immutable(char*) path, nuint count, PathStyle style) nothrow
    {
        if (count == 0) return false;
        if (style == PathStyle::Unix) return path[0] == '/';
        if (Paths::IsSeparator(path[0], style))
            return count >= 2 && Paths::IsSeparator(path[1], style);
        if (count < 3) return false;
        bool letter = (path[0] >= 'a' && path[0] <= 'z') || (path[0] >= 'A' && path[0] <= 'Z');
        return letter && path[1] == ':' && Paths::IsSeparator(path[2], style);
    }

    private static String JoinCore(immutable(char*) left, nuint leftLength,
        immutable(char*) right, nuint rightLength, PathStyle style)
    {
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

    private static nuint Length(immutable(char*) text) nothrow
    {
        nuint length = 0;
        while (text[length] != '\0') length++;
        return length;
    }

    private static nuint FileNameStart(immutable(char*) path, nuint length, PathStyle style)
    {
        nuint root = Paths::RootLength(path, length, style);
        nuint start = length;
        while (start > root && !Paths::IsSeparator(path[start - 1], style)) start--;
        return start;
    }

    private static nuint ExtensionStart(immutable(char*) path, nuint start, nuint end)
    {
        nuint index = end;
        while (index > start)
        {
            index--;
            if (path[index] == '.') return index;
        }
        return end;
    }

    private static nuint RootLength(immutable(char*) path, nuint length, PathStyle style)
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
