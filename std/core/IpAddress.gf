namespace std;

// Numeric address only: no port, zone, network prefix, or operating-system state.
public struct IpAddress : IEquatable<IpAddress>, IComparable<IpAddress>, IHashable, IFormattable
{
    private ulong high;
    private ulong low;
    private bool ipv4;

    public IpAddress() nothrow { high = 0ul; low = 0ul; ipv4 = true; }
    public bool IsIpv4 { immutable get nothrow => ipv4; }

    private static int HexDigit(char value) nothrow
    {
        if (value >= '0' && value <= '9') return value - '0';
        if (value >= 'a' && value <= 'f') return value - 'a' + 10;
        if (value >= 'A' && value <= 'F') return value - 'A' + 10;
        return -1;
    }

    private static bool ReadIpv4(readonly(char)* text, nuint start, nuint end, ref uint value) nothrow
    {
        value = 0u;
        nuint position = start;
        for (int part = 0; part < 4; part++)
        {
            nuint first = position;
            uint octet = 0u;
            while (position < end && text[position] >= '0' && text[position] <= '9')
            {
                // Reject before accumulating a fourth digit or a leading zero.
                if (position - first == 3 || (position > first && text[first] == '0')) return false;
                octet = octet * 10u + (uint)(text[position] - '0');
                position++;
            }
            if (position == first || octet > 255u) return false;
            value = (value << 8) | octet;
            if (part < 3)
            {
                if (position == end || text[position] != '.') return false;
                position++;
            }
        }
        return position == end;
    }

    // Only called with a nonempty input of at most 45 bytes. Output is a local
    // value owned by TryParse, so failures never modify the caller's input.
    private static bool ReadAddress(readonly(char)* text, nuint count, IpAddress* parsed) nothrow
    {
        bool hasColon = false;
        for (nuint i = 0; i < count; i++) if (text[i] == ':') hasColon = true;
        if (!hasColon)
        {
            uint value = 0u;
            if (!ReadIpv4(text, 0, count, ref value)) return false;
            parsed.low = (ulong)value;
            return true;
        }

        int[8] words;
        int used = 0;
        int compressed = -1;
        nuint position = 0;
        if (text[0] == ':')
        {
            if (count < 2 || text[1] != ':') return false;
            compressed = 0;
            position = 2;
        }
        while (position < count)
        {
            if (used == 8) return false;
            nuint end = position;
            bool dotted = false;
            while (end < count && text[end] != ':')
            {
                if (text[end] == '.') dotted = true;
                end++;
            }
            if (dotted)
            {
                if (end != count || used > 6) return false;
                uint tail = 0u;
                if (!ReadIpv4(text, position, end, ref tail)) return false;
                words[used++] = (int)(tail >> 16);
                words[used++] = (int)(tail & 65535u);
                position = end;
            }
            else
            {
                if (end == position || end - position > 4) return false;
                int word = 0;
                for (nuint i = position; i < end; i++)
                {
                    int digit = HexDigit(text[i]);
                    if (digit < 0) return false;
                    word = word * 16 + digit;
                }
                words[used++] = word;
                position = end;
                if (position < count)
                {
                    position++;
                    if (position == count) return false;
                    if (text[position] == ':')
                    {
                        if (compressed >= 0) return false;
                        compressed = used;
                        position++;
                    }
                }
            }
        }
        if (compressed >= 0)
        {
            if (used >= 8) return false;
            int omitted = 8 - used;
            for (int i = used - 1; i >= compressed; i--) words[i + omitted] = words[i];
            for (int i = compressed; i < compressed + omitted; i++) words[i] = 0;
        }
        else if (used != 8) return false;

        ulong upper = 0ul;
        ulong lower = 0ul;
        for (int i = 0; i < 4; i++)
        {
            upper = (upper << 16) | (ulong)words[i];
            lower = (lower << 16) | (ulong)words[i + 4];
        }
        parsed.high = upper;
        parsed.low = lower;
        parsed.ipv4 = false;
        return true;
    }

    // Commit only after parsing finishes; aligned result storage may overlap input.
    public static bool TryParse(readonly(char)* text, nuint count, IpAddress* result) nothrow
    {
        IpAddress parsed = new();
        if (count == 0 || count > 45) { *result = parsed; return false; }
        bool success = ReadAddress(text, count, &parsed);
        if (!success) parsed = new();
        *result = parsed;
        return success;
    }

    public static bool TryParse(readonly(char)* text, IpAddress* result) nothrow
    {
        nuint count = 0;
        while (count < 46 && text[count] != '\0') count++;
        return TryParse(text, count, result);
    }

    public static IpAddress Parse(readonly(char)* text, nuint count)
    {
        IpAddress result = new();
        if (!TryParse(text, count, &result)) throw new Exception("Invalid IP address");
        return result;
    }

    public static IpAddress Parse(readonly(char)* text)
    {
        IpAddress result = new();
        if (!TryParse(text, &result)) throw new Exception("Invalid IP address");
        return result;
    }

    public immutable bool Equals(immutable(IpAddress)* other) nothrow
        => ipv4 == other.ipv4 && high == other.high && low == other.low;

    public immutable int CompareTo(immutable(IpAddress)* other) nothrow
    {
        if (ipv4 != other.ipv4) { if (ipv4) return -1; return 1; }
        if (high < other.high) return -1;
        if (high > other.high) return 1;
        if (low < other.low) return -1;
        if (low > other.low) return 1;
        return 0;
    }

    public immutable nuint GetHashCode() nothrow
    {
        nuint family = 6;
        if (ipv4) family = 4;
        return Hashing::Combine(family, Hashing::Combine(Hashing::Integer(high), Hashing::Integer(low)));
    }

    private static void WriteIpv4(char* output, ref nuint count, uint value) nothrow
    {
        for (int part = 0; part < 4; part++)
        {
            uint octet = (value >> ((3 - part) * 8)) & 255u;
            if (part != 0) output[count++] = '.';
            if (octet >= 100u) output[count++] = (char)('0' + (int)(octet / 100u));
            if (octet >= 10u) output[count++] = (char)('0' + (int)(octet / 10u % 10u));
            output[count++] = (char)('0' + (int)(octet % 10u));
        }
    }

    // Counted ASCII without a terminator, at most 39 bytes. Stage the complete
    // representation before touching output, which may overlap this receiver.
    // written must be separate from output and the receiver's storage.
    public immutable bool TryFormat(char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        char[39] buffer;
        nuint count = 0;
        if (ipv4) WriteIpv4(buffer, ref count, (uint)low);
        else if (high == 0ul && (low >> 32) == 65535ul)
        {
            readonly(char)* prefix = "::ffff:";
            for (nuint i = 0; i < 7; i++) buffer[count++] = prefix[i];
            WriteIpv4(buffer, ref count, (uint)low);
        }
        else
        {
            int[8] words;
            for (int i = 0; i < 4; i++)
            {
                words[i] = (int)((high >> ((3 - i) * 16)) & 65535ul);
                words[i + 4] = (int)((low >> ((3 - i) * 16)) & 65535ul);
            }
            int bestStart = -1;
            int bestLength = 1;
            int start = 0;
            while (start < 8)
            {
                if (words[start] != 0) { start++; continue; }
                int end = start;
                while (end < 8 && words[end] == 0) end++;
                if (end - start > bestLength) { bestStart = start; bestLength = end - start; }
                start = end;
            }
            readonly(char)* digits = "0123456789abcdef";
            int index = 0;
            while (index < 8)
            {
                if (index == bestStart)
                {
                    buffer[count++] = ':';
                    buffer[count++] = ':';
                    index += bestLength;
                }
                else
                {
                    if (count != 0 && buffer[count - 1] != ':') buffer[count++] = ':';
                    int word = words[index++];
                    int shift = 12;
                    while (shift > 0 && ((word >> shift) & 15) == 0) shift -= 4;
                    while (shift >= 0)
                    {
                        buffer[count++] = digits[(word >> shift) & 15];
                        shift -= 4;
                    }
                }
            }
        }
        if (capacity < count) return false;
        for (nuint i = 0; i < count; i++) output[i] = buffer[i];
        written = count;
        return true;
    }

    public immutable void Format(ITextWriter* output)
    {
        char[39] buffer;
        nuint written = 0;
        this.TryFormat(buffer, 39, ref written);
        output.Write(buffer, written);
    }
}
