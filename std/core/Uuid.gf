namespace std;

// Opaque 128-bit identifier. Numeric halves follow canonical/network byte order.
public struct Uuid : IEquatable<Uuid>, IComparable<Uuid>, IHashable, IFormattable
{
    private ulong high;
    private ulong low;

    public Uuid() nothrow { high = 0ul; low = 0ul; }
    public Uuid(ulong upper, ulong lower) nothrow { high = upper; low = lower; }
    public ulong High { immutable get nothrow => high; }
    public ulong Low { immutable get nothrow => low; }
    public bool IsNil { immutable get nothrow => high == 0ul && low == 0ul; }

    // Canonical/network bytes: High first, then Low, most significant byte first.
    public static bool TryFromBytes(readonly(byte)* data, nuint count, Uuid* result) nothrow
    {
        Uuid parsed = new();
        if (count != 16) { *result = parsed; return false; }
        for (nuint i = 0; i < 8; i++)
        {
            parsed.high = (parsed.high << 8) | (ulong)data[i];
            parsed.low = (parsed.low << 8) | (ulong)data[i + 8];
        }
        *result = parsed;
        return true;
    }

    public static Uuid FromBytes(readonly(byte)* data, nuint count)
    {
        Uuid result = new();
        if (!TryFromBytes(data, count, &result)) throw new Exception("UUID requires 16 bytes");
        return result;
    }

    // Output may overlap this value; written is separate from output and receiver.
    public immutable bool TryWriteBytes(byte* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        if (capacity < 16) return false;
        byte[16] buffer;
        for (int i = 0; i < 8; i++)
        {
            buffer[i] = (byte)(high >> ((7 - i) * 8));
            buffer[i + 8] = (byte)(low >> ((7 - i) * 8));
        }
        for (nuint i = 0; i < 16; i++) output[i] = buffer[i];
        written = 16;
        return true;
    }

    private static int Digit(char value) nothrow
    {
        if (value >= '0' && value <= '9') return value - '0';
        if (value >= 'a' && value <= 'f') return value - 'a' + 10;
        if (value >= 'A' && value <= 'F') return value - 'A' + 10;
        return -1;
    }

    public static bool TryParse(readonly(char)* text, nuint count, Uuid* result) nothrow
    {
        Uuid parsed = new();
        if (count != 36) { *result = parsed; return false; }
        nuint digitCount = 0;
        for (nuint i = 0; i < 36; i++)
        {
            if (i == 8 || i == 13 || i == 18 || i == 23)
            {
                if (text[i] != '-') { *result = new Uuid(); return false; }
            }
            else
            {
                int digit = Uuid::Digit(text[i]);
                if (digit < 0) { *result = new Uuid(); return false; }
                if (digitCount < 16) parsed.high = (parsed.high << 4) | (ulong)digit;
                else parsed.low = (parsed.low << 4) | (ulong)digit;
                digitCount++;
            }
        }
        *result = parsed;
        return true;
    }

    public static bool TryParse(readonly(char)* text, Uuid* result) nothrow
    {
        nuint count = 0;
        while (count < 37 && text[count] != '\0') count++;
        return Uuid::TryParse(text, count, result);
    }

    public static Uuid Parse(readonly(char)* text, nuint count)
    {
        Uuid result = new();
        if (!Uuid::TryParse(text, count, &result)) throw new Exception("Invalid UUID; expected 8-4-4-4-12 hexadecimal digits");
        return result;
    }

    public static Uuid Parse(readonly(char)* text)
    {
        Uuid result = new();
        if (!Uuid::TryParse(text, &result)) throw new Exception("Invalid UUID; expected 8-4-4-4-12 hexadecimal digits");
        return result;
    }

    public immutable bool Equals(immutable(Uuid)* other) nothrow => high == other.high && low == other.low;
    public immutable int CompareTo(immutable(Uuid)* other) nothrow
    {
        if (high < other.high) return -1;
        if (high > other.high) return 1;
        if (low < other.low) return -1;
        if (low > other.low) return 1;
        return 0;
    }

    public immutable nuint GetHashCode() nothrow
    {
        char[16] bytes;
        for (int i = 0; i < 8; i++)
        {
            bytes[i] = (char)(byte)(high >> ((7 - i) * 8));
            bytes[i + 8] = (char)(byte)(low >> ((7 - i) * 8));
        }
        return Hashing::Integer((ulong)Hashing::Bytes(bytes, 16));
    }

    // Exactly 36 lowercase bytes, without a null terminator.
    // Written must be separate from output and this value's storage.
    public immutable bool TryFormat(char* output, nuint capacity, ref nuint written) nothrow
    {
        written = 0;
        if (capacity < 36) return false;
        ulong upper = high; ulong lower = low;
        readonly(char)* digits = "0123456789abcdef";
        int digitCount = 0;
        for (nuint i = 0; i < 36; i++)
        {
            if (i == 8 || i == 13 || i == 18 || i == 23) output[i] = '-';
            else
            {
                ulong value = upper;
                if (digitCount >= 16) value = lower;
                output[i] = digits[(int)((value >> ((15 - (digitCount % 16)) * 4)) & 15ul)];
                digitCount++;
            }
        }
        written = 36;
        return true;
    }

    public immutable void Format(ITextWriter* output)
    {
        char[36] buffer; nuint written = 0;
        this.TryFormat(buffer, 36, ref written);
        output.Write(buffer, written);
    }
}
