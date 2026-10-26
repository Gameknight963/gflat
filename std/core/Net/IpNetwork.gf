using std::Collections;
namespace std;

// A canonical numeric prefix; no socket or operating-system state.
public struct IpNetwork : IEquatable<IpNetwork>, IHashable, IFormattable
{
    private IpAddress networkAddress;
    private int prefixLength;

    public IpNetwork() nothrow { networkAddress = new(); prefixLength = 0; }
    public IpNetwork(IpAddress networkAddress, int prefixLength)
    {
        this.networkAddress = new(); this.prefixLength = 0;
        if (!ValidPrefix(networkAddress, prefixLength))
            throw new Exception("IP network prefix is out of range");
        IpAddress masked = MaskAddress(networkAddress, prefixLength);
        if (!networkAddress.Equals(&masked))
            throw new Exception("IP network address has host bits");
        this.networkAddress = networkAddress; this.prefixLength = prefixLength;
    }
    public IpAddress NetworkAddress { immutable get nothrow => networkAddress; }
    // Numeric range endpoint, including every host bit; no host-usability policy.
    public IpAddress LastAddress
    {
        immutable get nothrow
        {
            byte[16] bytes;
            nuint count = 0;
            networkAddress.TryWriteBytes(bytes, 16, ref count);
            int remaining = prefixLength;
            for (nuint i = 0; i < count; i++)
            {
                bytes[i] = bytes[i] | (byte)(255u - (uint)PrefixMask(remaining));
                remaining -= 8;
            }
            IpAddress result = new();
            IpAddress::TryFromBytes(bytes, count, &result);
            return result;
        }
    }
    public int PrefixLength { immutable get nothrow => prefixLength; }

    private static bool ValidPrefix(IpAddress address, int prefix) nothrow
    {
        int maximum = 128;
        if (address.IsIpv4) maximum = 32;
        return prefix >= 0 && prefix <= maximum;
    }
    private static byte PrefixMask(int remaining) nothrow
    {
        if (remaining <= 0) return (byte)0;
        if (remaining >= 8) return (byte)255;
        return (byte)(255u << (8 - remaining));
    }
    private static IpAddress MaskAddress(IpAddress address, int prefix) nothrow
    {
        byte[16] bytes;
        nuint written = 0;
        address.TryWriteBytes(bytes, 16, ref written);
        for (nuint i = 0; i < written; i++)
        {
            bytes[i] = bytes[i] & PrefixMask(prefix);
            prefix -= 8;
        }
        IpAddress result = new();
        IpAddress::TryFromBytes(bytes, written, &result);
        return result;
    }

    public static IpNetwork FromAddress(IpAddress address, int prefixLength)
    {
        if (!ValidPrefix(address, prefixLength))
            throw new Exception("IP network prefix is out of range");
        IpNetwork result = new();
        result.networkAddress = MaskAddress(address, prefixLength);
        result.prefixLength = prefixLength;
        return result;
    }
    public immutable bool Contains(immutable(IpAddress)* address) nothrow
    {
        if (networkAddress.IsIpv4 != address.IsIpv4) return false;
        byte[16] first; byte[16] second;
        nuint count = 0; nuint written = 0;
        networkAddress.TryWriteBytes(first, 16, ref count);
        address.TryWriteBytes(second, 16, ref written);
        int remaining = prefixLength;
        for (nuint i = 0; i < count; i++)
        {
            if ((second[i] & PrefixMask(remaining)) != first[i]) return false;
            remaining -= 8;
        }
        return true;
    }

    public immutable bool Contains(immutable(IpNetwork)* other) nothrow
    {
        return prefixLength <= other.prefixLength && this.Contains(&other.networkAddress);
    }

    public immutable bool Overlaps(immutable(IpNetwork)* other) nothrow
    {
        if (prefixLength <= other.prefixLength) return this.Contains(&other.networkAddress);
        return other.Contains(&networkAddress);
    }

    private static bool ParseFailure(IpNetwork* result) nothrow
    {
        *result = new IpNetwork();
        return false;
    }
    public static bool TryParse(readonly(char)* text, nuint count, IpNetwork* result) nothrow
    {
        if (count == 0 || count > 49) return ParseFailure(result);
        nuint slash = count;
        for (nuint i = 0; i < count; i++)
        {
            if (text[i] == '/')
            {
                if (slash != count) return ParseFailure(result);
                slash = i;
            }
        }
        if (slash == 0 || slash == count) return ParseFailure(result);
        nuint digits = count - slash - 1;
        if (digits == 0 || digits > 3) return ParseFailure(result);
        int prefix = 0;
        for (nuint i = slash + 1; i < count; i++)
        {
            if (text[i] < '0' || text[i] > '9') return ParseFailure(result);
            prefix = prefix * 10 + (text[i] - '0');
        }
        IpAddress address = new();
        if (!IpAddress::TryParse(text, slash, &address) || !ValidPrefix(address, prefix))
            return ParseFailure(result);
        IpAddress masked = MaskAddress(address, prefix);
        if (!address.Equals(&masked)) return ParseFailure(result);
        IpNetwork parsed = new();
        parsed.networkAddress = address; parsed.prefixLength = prefix;
        *result = parsed;
        return true;
    }
    public static bool TryParse(readonly(char)* text, IpNetwork* result) nothrow
    {
        nuint count = 0;
        while (count < 50 && text[count] != '\0') count++;
        return TryParse(text, count, result);
    }
    public static IpNetwork Parse(readonly(char)* text, nuint count)
    {
        IpNetwork result = new();
        if (!TryParse(text, count, &result)) throw new Exception("Invalid IP network");
        return result;
    }
    public static IpNetwork Parse(readonly(char)* text)
    {
        IpNetwork result = new();
        if (!TryParse(text, &result)) throw new Exception("Invalid IP network");
        return result;
    }

    public immutable bool Equals(immutable(IpNetwork)* other) nothrow
        => prefixLength == other.prefixLength && networkAddress.Equals(&other.networkAddress);
    public immutable nuint GetHashCode() nothrow
        => Hashing::Combine(networkAddress.GetHashCode(), Hashing::Integer((ulong)prefixLength));

    // Stage all bytes before copying, so a fully backed output may alias this value.
    // written must be separate from output and receiver storage.
    public immutable bool TryFormat(char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        char[43] buffer;
        nuint count = 0;
        networkAddress.TryFormat(buffer, 39, ref count);
        buffer[count++] = '/';
        if (prefixLength >= 100) buffer[count++] = (char)('0' + prefixLength / 100);
        if (prefixLength >= 10) buffer[count++] = (char)('0' + prefixLength / 10 % 10);
        buffer[count++] = (char)('0' + prefixLength % 10);
        if (capacity < count) return false;
        for (nuint i = 0; i < count; i++) output[i] = buffer[i];
        written = count;
        return true;
    }
    public immutable void Format(ITextWriter* output)
    {
        char[43] buffer; nuint written = 0;
        this.TryFormat(buffer, 43, ref written);
        output.Write(buffer, written);
    }
}
