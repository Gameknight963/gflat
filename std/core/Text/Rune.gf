using std::Text;
namespace std::Text;

// A validated Unicode scalar, formatted as its counted UTF-8 encoding.
public struct Rune : IEquatable<Rune>, IComparable<Rune>, IHashable, IFormattable, IStringConvertable
{
    private uint value;

    public Rune() nothrow { value = 0u; }
    public Rune(uint scalar)
    {
        if (!Utf8::IsValidScalar(scalar))
            throw new Exception("Invalid Unicode scalar value");
        value = scalar;
    }

    public uint Value { immutable get nothrow => value; }
    public bool IsAscii { immutable get nothrow => value < 0x80u; }
    public nuint Utf8Length { immutable get nothrow => Utf8::EncodedLength(value); }
    public nuint Utf16Length { immutable get nothrow => Utf16::EncodedLength(value); }

    public immutable bool Equals(immutable(Rune)* other) nothrow => value == other.value;
    public immutable int CompareTo(immutable(Rune)* other) nothrow
    {
        if (value < other.value) return -1;
        if (value > other.value) return 1;
        return 0;
    }
    public immutable nuint GetHashCode() nothrow => Hashing::Integer((ulong)value);

    public immutable void Format(ITextWriter* output)
    {
        char[4] bytes;
        nuint written = 0;
        Utf8::TryEncode(value, bytes, 4, &written);
        output.Write(bytes, written);
    }

    public immutable String ToString()
    {
        String result = new();
        this.Format(&result);
        return move result;
    }
}
