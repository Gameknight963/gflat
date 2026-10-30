using Standard::Memory;
namespace Standard::Text;
byte ParseByte(Span<readonly(char)> text)
=> text.ParseByte();

extension Span<readonly(char)>
{
    public immutable byte ParseByte()
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::ParseByte("", 0);
        return Standard::Text::ParseByte(protectedView.Data!, protectedView.Length);
    }
}

sbyte ParseSByte(Span<readonly(char)> text)
=> text.ParseSByte();

extension Span<readonly(char)>
{
    public immutable sbyte ParseSByte()
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::ParseSByte("", 0);
        return Standard::Text::ParseSByte(protectedView.Data!, protectedView.Length);
    }
}

short ParseShort(Span<readonly(char)> text)
=> text.ParseShort();

extension Span<readonly(char)>
{
    public immutable short ParseShort()
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::ParseShort("", 0);
        return Standard::Text::ParseShort(protectedView.Data!, protectedView.Length);
    }
}

ushort ParseUShort(Span<readonly(char)> text)
=> text.ParseUShort();

extension Span<readonly(char)>
{
    public immutable ushort ParseUShort()
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::ParseUShort("", 0);
        return Standard::Text::ParseUShort(protectedView.Data!, protectedView.Length);
    }
}

int ParseInt(Span<readonly(char)> text)
=> text.ParseInt();

extension Span<readonly(char)>
{
    public immutable int ParseInt()
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::ParseInt("", 0);
        return Standard::Text::ParseInt(protectedView.Data!, protectedView.Length);
    }
}

uint ParseUInt(Span<readonly(char)> text)
=> text.ParseUInt();

extension Span<readonly(char)>
{
    public immutable uint ParseUInt()
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::ParseUInt("", 0);
        return Standard::Text::ParseUInt(protectedView.Data!, protectedView.Length);
    }
}

long ParseLong(Span<readonly(char)> text)
=> text.ParseLong();

extension Span<readonly(char)>
{
    public immutable long ParseLong()
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::ParseLong("", 0);
        return Standard::Text::ParseLong(protectedView.Data!, protectedView.Length);
    }
}

ulong ParseULong(Span<readonly(char)> text)
=> text.ParseULong();

extension Span<readonly(char)>
{
    public immutable ulong ParseULong()
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::ParseULong("", 0);
        return Standard::Text::ParseULong(protectedView.Data!, protectedView.Length);
    }
}

extralong ParseExtraLong(Span<readonly(char)> text)
=> text.ParseExtraLong();

extension Span<readonly(char)>
{
    public immutable extralong ParseExtraLong()
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::ParseExtraLong("", 0);
        return Standard::Text::ParseExtraLong(protectedView.Data!, protectedView.Length);
    }
}

nint ParseNInt(Span<readonly(char)> text)
=> text.ParseNInt();

extension Span<readonly(char)>
{
    public immutable nint ParseNInt()
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::ParseNInt("", 0);
        return Standard::Text::ParseNInt(protectedView.Data!, protectedView.Length);
    }
}

nuint ParseNUInt(Span<readonly(char)> text)
=> text.ParseNUInt();

extension Span<readonly(char)>
{
    public immutable nuint ParseNUInt()
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::ParseNUInt("", 0);
        return Standard::Text::ParseNUInt(protectedView.Data!, protectedView.Length);
    }
}

