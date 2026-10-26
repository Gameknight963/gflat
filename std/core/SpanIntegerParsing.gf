using std::Memory;
namespace std;
bool TryParseByte(this Span<readonly(char)> text, byte* result) nothrow
{
    if (text.IsEmpty) return std::TryParseByte("", 0, result);
    return std::TryParseByte(text.Data!, text.Length, result);
}

bool TryParseSByte(this Span<readonly(char)> text, sbyte* result) nothrow
{
    if (text.IsEmpty) return std::TryParseSByte("", 0, result);
    return std::TryParseSByte(text.Data!, text.Length, result);
}

bool TryParseShort(this Span<readonly(char)> text, short* result) nothrow
{
    if (text.IsEmpty) return std::TryParseShort("", 0, result);
    return std::TryParseShort(text.Data!, text.Length, result);
}

bool TryParseUShort(this Span<readonly(char)> text, ushort* result) nothrow
{
    if (text.IsEmpty) return std::TryParseUShort("", 0, result);
    return std::TryParseUShort(text.Data!, text.Length, result);
}

bool TryParseInt(this Span<readonly(char)> text, int* result) nothrow
{
    if (text.IsEmpty) return std::TryParseInt("", 0, result);
    return std::TryParseInt(text.Data!, text.Length, result);
}

bool TryParseUInt(this Span<readonly(char)> text, uint* result) nothrow
{
    if (text.IsEmpty) return std::TryParseUInt("", 0, result);
    return std::TryParseUInt(text.Data!, text.Length, result);
}

bool TryParseLong(this Span<readonly(char)> text, long* result) nothrow
{
    if (text.IsEmpty) return std::TryParseLong("", 0, result);
    return std::TryParseLong(text.Data!, text.Length, result);
}

bool TryParseULong(this Span<readonly(char)> text, ulong* result) nothrow
{
    if (text.IsEmpty) return std::TryParseULong("", 0, result);
    return std::TryParseULong(text.Data!, text.Length, result);
}

bool TryParseExtraLong(this Span<readonly(char)> text, extralong* result) nothrow
{
    if (text.IsEmpty) return std::TryParseExtraLong("", 0, result);
    return std::TryParseExtraLong(text.Data!, text.Length, result);
}

bool TryParseNInt(this Span<readonly(char)> text, nint* result) nothrow
{
    if (text.IsEmpty) return std::TryParseNInt("", 0, result);
    return std::TryParseNInt(text.Data!, text.Length, result);
}

bool TryParseNUInt(this Span<readonly(char)> text, nuint* result) nothrow
{
    if (text.IsEmpty) return std::TryParseNUInt("", 0, result);
    return std::TryParseNUInt(text.Data!, text.Length, result);
}

