namespace std;
byte ParseByte(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::ParseByte("", 0);
    return std::ParseByte(text.Data!, text.Length);
}

sbyte ParseSByte(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::ParseSByte("", 0);
    return std::ParseSByte(text.Data!, text.Length);
}

short ParseShort(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::ParseShort("", 0);
    return std::ParseShort(text.Data!, text.Length);
}

ushort ParseUShort(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::ParseUShort("", 0);
    return std::ParseUShort(text.Data!, text.Length);
}

int ParseInt(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::ParseInt("", 0);
    return std::ParseInt(text.Data!, text.Length);
}

uint ParseUInt(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::ParseUInt("", 0);
    return std::ParseUInt(text.Data!, text.Length);
}

long ParseLong(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::ParseLong("", 0);
    return std::ParseLong(text.Data!, text.Length);
}

ulong ParseULong(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::ParseULong("", 0);
    return std::ParseULong(text.Data!, text.Length);
}

extralong ParseExtraLong(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::ParseExtraLong("", 0);
    return std::ParseExtraLong(text.Data!, text.Length);
}

nint ParseNInt(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::ParseNInt("", 0);
    return std::ParseNInt(text.Data!, text.Length);
}

nuint ParseNUInt(this Span<readonly(char)> text)
{
    if (text.IsEmpty) return std::ParseNUInt("", 0);
    return std::ParseNUInt(text.Data!, text.Length);
}

