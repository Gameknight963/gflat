using std::Memory;
namespace std::Text;
bool TryParseByte(this Span<readonly(char)> text, byte* result) nothrow
{
    if (text.IsEmpty) return std::Text::TryParseByte("", 0, result);
    return std::Text::TryParseByte(text.Data!, text.Length, result);
}

bool TryParseSByte(this Span<readonly(char)> text, sbyte* result) nothrow
{
    if (text.IsEmpty) return std::Text::TryParseSByte("", 0, result);
    return std::Text::TryParseSByte(text.Data!, text.Length, result);
}

bool TryParseShort(this Span<readonly(char)> text, short* result) nothrow
{
    if (text.IsEmpty) return std::Text::TryParseShort("", 0, result);
    return std::Text::TryParseShort(text.Data!, text.Length, result);
}

bool TryParseUShort(this Span<readonly(char)> text, ushort* result) nothrow
{
    if (text.IsEmpty) return std::Text::TryParseUShort("", 0, result);
    return std::Text::TryParseUShort(text.Data!, text.Length, result);
}

bool TryParseInt(this Span<readonly(char)> text, int* result) nothrow
{
    if (text.IsEmpty) return std::Text::TryParseInt("", 0, result);
    return std::Text::TryParseInt(text.Data!, text.Length, result);
}

bool TryParseUInt(this Span<readonly(char)> text, uint* result) nothrow
{
    if (text.IsEmpty) return std::Text::TryParseUInt("", 0, result);
    return std::Text::TryParseUInt(text.Data!, text.Length, result);
}

bool TryParseLong(this Span<readonly(char)> text, long* result) nothrow
{
    if (text.IsEmpty) return std::Text::TryParseLong("", 0, result);
    return std::Text::TryParseLong(text.Data!, text.Length, result);
}

bool TryParseULong(this Span<readonly(char)> text, ulong* result) nothrow
{
    if (text.IsEmpty) return std::Text::TryParseULong("", 0, result);
    return std::Text::TryParseULong(text.Data!, text.Length, result);
}

bool TryParseExtraLong(this Span<readonly(char)> text, extralong* result) nothrow
{
    if (text.IsEmpty) return std::Text::TryParseExtraLong("", 0, result);
    return std::Text::TryParseExtraLong(text.Data!, text.Length, result);
}

bool TryParseNInt(this Span<readonly(char)> text, nint* result) nothrow
{
    if (text.IsEmpty) return std::Text::TryParseNInt("", 0, result);
    return std::Text::TryParseNInt(text.Data!, text.Length, result);
}

bool TryParseNUInt(this Span<readonly(char)> text, nuint* result) nothrow
{
    if (text.IsEmpty) return std::Text::TryParseNUInt("", 0, result);
    return std::Text::TryParseNUInt(text.Data!, text.Length, result);
}

