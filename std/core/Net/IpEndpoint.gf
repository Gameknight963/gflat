namespace std::Net;

// Numeric address and port only; no zone, socket, or operating-system state.
public struct IpEndpoint : IEquatable<IpEndpoint>, IHashable, IFormattable
{
    private IpAddress address;
    private ushort port;

    public IpEndpoint() nothrow { address = new(); port = (ushort)0; }
    public IpEndpoint(IpAddress address, ushort port) nothrow
    {
        this.address = address; this.port = port;
    }
    public IpAddress Address { immutable get nothrow => address; }
    public ushort Port { immutable get nothrow => port; }

    private static bool ParseFailure(IpEndpoint* result) nothrow
    {
        *result = new IpEndpoint();
        return false;
    }

    // Parse into locals before committing: aligned result storage may overlap
    // fully backed readable input. Failure also resets the complete result.
    public static bool TryParse(readonly(char)* text, nuint count, IpEndpoint* result) nothrow
    {
        if (count == 0 || count > 53) return ParseFailure(result);
        bool bracketed = text[0] == '[';
        nuint start = 0;
        nuint end = count;
        nuint digits = count;
        if (bracketed)
        {
            start = 1;
            for (nuint i = 1; i < count; i++)
            {
                if (text[i] == ']') { end = i; break; }
            }
            if (end == count || end == start || end + 1 >= count || text[end + 1] != ':')
                return ParseFailure(result);
            digits = end + 2;
        }
        else
        {
            for (nuint i = 0; i < count; i++)
            {
                if (text[i] == ':') { end = i; break; }
            }
            if (end == 0 || end == count) return ParseFailure(result);
            digits = end + 1;
        }
        nuint length = count - digits;
        if (length == 0 || length > 5) return ParseFailure(result);
        uint number = 0u;
        for (nuint i = digits; i < count; i++)
        {
            if (text[i] < '0' || text[i] > '9') return ParseFailure(result);
            number = number * 10u + (uint)(text[i] - '0');
        }
        if (number > 65535u) return ParseFailure(result);
        IpAddress parsedAddress = new();
        if (!IpAddress::TryParse(text + start, end - start, &parsedAddress) ||
            parsedAddress.IsIpv4 == bracketed) return ParseFailure(result);
        IpEndpoint parsed = new(parsedAddress, (ushort)number);
        *result = parsed;
        return true;
    }
    public static bool TryParse(readonly(char)* text, IpEndpoint* result) nothrow
    {
        nuint count = 0;
        while (count < 54 && text[count] != '\0') count++;
        return TryParse(text, count, result);
    }
    public static IpEndpoint Parse(readonly(char)* text, nuint count)
    {
        IpEndpoint result = new();
        if (!TryParse(text, count, &result)) throw new Exception("Invalid IP endpoint");
        return result;
    }
    public static IpEndpoint Parse(readonly(char)* text)
    {
        IpEndpoint result = new();
        if (!TryParse(text, &result)) throw new Exception("Invalid IP endpoint");
        return result;
    }

    public immutable bool Equals(immutable(IpEndpoint)* other) nothrow
        => port == other.port && address.Equals(&other.address);
    public immutable nuint GetHashCode() nothrow
        => Hashing::Combine(address.GetHashCode(), Hashing::Integer((ulong)port));

    // Counted ASCII without a terminator, at most 47 bytes. Stage the complete
    // representation before touching output, which may overlap this receiver.
    // written must be separate from output and receiver storage.
    public immutable bool TryFormat(char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        char[47] buffer;
        nuint count = 0;
        if (!address.IsIpv4) buffer[count++] = '[';
        nuint addressLength = 0;
        address.TryFormat(&buffer[count], 39, ref addressLength);
        count += addressLength;
        if (!address.IsIpv4) buffer[count++] = ']';
        buffer[count++] = ':';
        uint number = (uint)port;
        if (number >= 10000u) buffer[count++] = (char)('0' + (int)(number / 10000u));
        if (number >= 1000u) buffer[count++] = (char)('0' + (int)(number / 1000u % 10u));
        if (number >= 100u) buffer[count++] = (char)('0' + (int)(number / 100u % 10u));
        if (number >= 10u) buffer[count++] = (char)('0' + (int)(number / 10u % 10u));
        buffer[count++] = (char)('0' + (int)(number % 10u));
        if (capacity < count) return false;
        for (nuint i = 0; i < count; i++) output[i] = buffer[i];
        written = count;
        return true;
    }
    public immutable void Format(ITextWriter* output)
    {
        char[47] buffer;
        nuint written = 0;
        this.TryFormat(buffer, 47, ref written);
        output.Write(buffer, written);
    }
}
