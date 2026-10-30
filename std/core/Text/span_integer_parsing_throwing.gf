using std::Memory;
namespace std::Text;
byte ParseByte(Span<readonly(char)> text)
=> text.ParseByte();

extension Span<readonly(char)>
{
    public byte ParseByte()
    {
        if ((*this).IsEmpty) return std::Text::ParseByte("", 0);
        return std::Text::ParseByte((*this).Data!, (*this).Length);
    }
}

sbyte ParseSByte(Span<readonly(char)> text)
=> text.ParseSByte();

extension Span<readonly(char)>
{
    public sbyte ParseSByte()
    {
        if ((*this).IsEmpty) return std::Text::ParseSByte("", 0);
        return std::Text::ParseSByte((*this).Data!, (*this).Length);
    }
}

short ParseShort(Span<readonly(char)> text)
=> text.ParseShort();

extension Span<readonly(char)>
{
    public short ParseShort()
    {
        if ((*this).IsEmpty) return std::Text::ParseShort("", 0);
        return std::Text::ParseShort((*this).Data!, (*this).Length);
    }
}

ushort ParseUShort(Span<readonly(char)> text)
=> text.ParseUShort();

extension Span<readonly(char)>
{
    public ushort ParseUShort()
    {
        if ((*this).IsEmpty) return std::Text::ParseUShort("", 0);
        return std::Text::ParseUShort((*this).Data!, (*this).Length);
    }
}

int ParseInt(Span<readonly(char)> text)
=> text.ParseInt();

extension Span<readonly(char)>
{
    public int ParseInt()
    {
        if ((*this).IsEmpty) return std::Text::ParseInt("", 0);
        return std::Text::ParseInt((*this).Data!, (*this).Length);
    }
}

uint ParseUInt(Span<readonly(char)> text)
=> text.ParseUInt();

extension Span<readonly(char)>
{
    public uint ParseUInt()
    {
        if ((*this).IsEmpty) return std::Text::ParseUInt("", 0);
        return std::Text::ParseUInt((*this).Data!, (*this).Length);
    }
}

long ParseLong(Span<readonly(char)> text)
=> text.ParseLong();

extension Span<readonly(char)>
{
    public long ParseLong()
    {
        if ((*this).IsEmpty) return std::Text::ParseLong("", 0);
        return std::Text::ParseLong((*this).Data!, (*this).Length);
    }
}

ulong ParseULong(Span<readonly(char)> text)
=> text.ParseULong();

extension Span<readonly(char)>
{
    public ulong ParseULong()
    {
        if ((*this).IsEmpty) return std::Text::ParseULong("", 0);
        return std::Text::ParseULong((*this).Data!, (*this).Length);
    }
}

extralong ParseExtraLong(Span<readonly(char)> text)
=> text.ParseExtraLong();

extension Span<readonly(char)>
{
    public extralong ParseExtraLong()
    {
        if ((*this).IsEmpty) return std::Text::ParseExtraLong("", 0);
        return std::Text::ParseExtraLong((*this).Data!, (*this).Length);
    }
}

nint ParseNInt(Span<readonly(char)> text)
=> text.ParseNInt();

extension Span<readonly(char)>
{
    public nint ParseNInt()
    {
        if ((*this).IsEmpty) return std::Text::ParseNInt("", 0);
        return std::Text::ParseNInt((*this).Data!, (*this).Length);
    }
}

nuint ParseNUInt(Span<readonly(char)> text)
=> text.ParseNUInt();

extension Span<readonly(char)>
{
    public nuint ParseNUInt()
    {
        if ((*this).IsEmpty) return std::Text::ParseNUInt("", 0);
        return std::Text::ParseNUInt((*this).Data!, (*this).Length);
    }
}

