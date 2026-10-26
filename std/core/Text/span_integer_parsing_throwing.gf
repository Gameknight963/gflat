using std::Memory;
namespace std::Text;
byte ParseByte(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::Text::ParseByte("", 0);
    return std::Text::ParseByte(text.Data!, text.Length);
}

sbyte ParseSByte(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::Text::ParseSByte("", 0);
    return std::Text::ParseSByte(text.Data!, text.Length);
}

short ParseShort(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::Text::ParseShort("", 0);
    return std::Text::ParseShort(text.Data!, text.Length);
}

ushort ParseUShort(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::Text::ParseUShort("", 0);
    return std::Text::ParseUShort(text.Data!, text.Length);
}

int ParseInt(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::Text::ParseInt("", 0);
    return std::Text::ParseInt(text.Data!, text.Length);
}

uint ParseUInt(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::Text::ParseUInt("", 0);
    return std::Text::ParseUInt(text.Data!, text.Length);
}

long ParseLong(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::Text::ParseLong("", 0);
    return std::Text::ParseLong(text.Data!, text.Length);
}

ulong ParseULong(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::Text::ParseULong("", 0);
    return std::Text::ParseULong(text.Data!, text.Length);
}

extralong ParseExtraLong(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::Text::ParseExtraLong("", 0);
    return std::Text::ParseExtraLong(text.Data!, text.Length);
}

nint ParseNInt(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::Text::ParseNInt("", 0);
    return std::Text::ParseNInt(text.Data!, text.Length);
}

nuint ParseNUInt(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::Text::ParseNUInt("", 0);
    return std::Text::ParseNUInt(text.Data!, text.Length);
}

