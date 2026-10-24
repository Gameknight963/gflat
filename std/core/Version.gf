namespace std;

// Dotted numeric version, not Semantic Versioning. Missing trailing parts are -1.
public struct Version : IEquatable<Version>, IComparable<Version>, IHashable, IFormattable
{
    private int major;
    private int minor;
    private int build;
    private int revision;

    public Version() nothrow { major = 0; minor = 0; build = -1; revision = -1; }
    public Version(int major, int minor, int build = -1, int revision = -1)
    {
        if (major < 0 || minor < 0 || build < -1 || revision < -1 || (build == -1 && revision != -1))
            throw new Exception("Invalid version components");
        this.major = major; this.minor = minor; this.build = build; this.revision = revision;
    }

    public int Major { immutable get nothrow => major; }
    public int Minor { immutable get nothrow => minor; }
    public int Build { immutable get nothrow => build; }
    public int Revision { immutable get nothrow => revision; }

    public immutable bool Equals(immutable(Version)* other) nothrow
        => major == other.major && minor == other.minor && build == other.build && revision == other.revision;
    public immutable int CompareTo(immutable(Version)* other) nothrow
    {
        if (major < other.major) return -1;
        if (major > other.major) return 1;
        if (minor < other.minor) return -1;
        if (minor > other.minor) return 1;
        if (build < other.build) return -1;
        if (build > other.build) return 1;
        if (revision < other.revision) return -1;
        if (revision > other.revision) return 1;
        return 0;
    }
    public immutable nuint GetHashCode() nothrow
    {
        nuint first = Hashing::Combine(Hashing::Integer((ulong)(uint)major), Hashing::Integer((ulong)(uint)minor));
        nuint second = Hashing::Combine(Hashing::Integer((ulong)(uint)build), Hashing::Integer((ulong)(uint)revision));
        return Hashing::Combine(first, second);
    }

    private static bool TryRead(readonly(char)* text, nuint count, ref Version parsed) nothrow
    {
        if (count < 3) return false;
        int[4] parts = [0, 0, -1, -1];
        int component = 0;
        nuint position = 0;
        while (position < count)
        {
            if (component == 4 || text[position] < '0' || text[position] > '9') return false;
            int value = 0;
            while (position < count && text[position] >= '0' && text[position] <= '9')
            {
                int digit = (int)text[position] - (int)'0';
                if (value > 214748364 || (value == 214748364 && digit > 7)) return false;
                value = value * 10 + digit;
                position++;
            }
            parts[component] = value;
            component++;
            if (position == count) break;
            if (text[position] != '.') return false;
            position++;
            if (position == count) return false;
        }
        if (component < 2) return false;
        parsed.major = parts[0]; parsed.minor = parts[1];
        parsed.build = parts[2]; parsed.revision = parts[3];
        return true;
    }

    // Input reads precede the result store on both paths.
    public static bool TryParse(readonly(char)* text, nuint count, Version* result) nothrow
    {
        Version parsed = new();
        bool success = TryRead(text, count, ref parsed);
        *result = parsed;
        return success;
    }
    public static bool TryParse(readonly(char)* text, Version* result) nothrow
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        return TryParse(text, count, result);
    }
    public static Version Parse(readonly(char)* text, nuint count)
    {
        Version result = new();
        if (!TryParse(text, count, &result)) throw new Exception("Invalid version");
        return result;
    }
    public static Version Parse(readonly(char)* text)
    {
        Version result = new();
        if (!TryParse(text, &result)) throw new Exception("Invalid version");
        return result;
    }

    private static void WritePart(int value, char* output, ref nuint length) nothrow
    {
        int divisor = 1;
        while (value / divisor >= 10) divisor *= 10;
        while (divisor != 0)
        {
            output[length] = (char)((int)'0' + value / divisor);
            length++;
            value %= divisor;
            divisor /= 10;
        }
    }
    // Staging supports sufficiently backed receiver/output overlap. Written is separate.
    public immutable bool TryFormat(char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        char[43] staged;
        nuint length = 0;
        WritePart(major, staged, ref length); staged[length] = '.'; length++;
        WritePart(minor, staged, ref length);
        if (build >= 0)
        {
            staged[length] = '.'; length++;
            WritePart(build, staged, ref length);
        }
        if (revision >= 0)
        {
            staged[length] = '.'; length++;
            WritePart(revision, staged, ref length);
        }
        if (capacity < length) return false;
        for (nuint i = 0; i < length; i++) output[i] = staged[i];
        written = length;
        return true;
    }
    public immutable void Format(ITextWriter* output)
    {
        char[43] buffer;
        nuint written = 0;
        this.TryFormat(buffer, 43, ref written);
        output.Write(buffer, written);
    }
}
