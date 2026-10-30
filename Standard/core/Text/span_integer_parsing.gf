using Standard::Memory;
namespace Standard::Text;
bool TryParseByte(Span<readonly(char)> text, byte* result) nothrow
=> text.TryParseByte(result);

extension Span<readonly(char)>
{
    public immutable bool TryParseByte(byte* result) nothrow
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::TryParseByte("", 0, result);
        return Standard::Text::TryParseByte(protectedView.Data!, protectedView.Length, result);
    }
}

bool TryParseSByte(Span<readonly(char)> text, sbyte* result) nothrow
=> text.TryParseSByte(result);

extension Span<readonly(char)>
{
    public immutable bool TryParseSByte(sbyte* result) nothrow
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::TryParseSByte("", 0, result);
        return Standard::Text::TryParseSByte(protectedView.Data!, protectedView.Length, result);
    }
}

bool TryParseShort(Span<readonly(char)> text, short* result) nothrow
=> text.TryParseShort(result);

extension Span<readonly(char)>
{
    public immutable bool TryParseShort(short* result) nothrow
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::TryParseShort("", 0, result);
        return Standard::Text::TryParseShort(protectedView.Data!, protectedView.Length, result);
    }
}

bool TryParseUShort(Span<readonly(char)> text, ushort* result) nothrow
=> text.TryParseUShort(result);

extension Span<readonly(char)>
{
    public immutable bool TryParseUShort(ushort* result) nothrow
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::TryParseUShort("", 0, result);
        return Standard::Text::TryParseUShort(protectedView.Data!, protectedView.Length, result);
    }
}

bool TryParseInt(Span<readonly(char)> text, int* result) nothrow
=> text.TryParseInt(result);

extension Span<readonly(char)>
{
    public immutable bool TryParseInt(int* result) nothrow
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::TryParseInt("", 0, result);
        return Standard::Text::TryParseInt(protectedView.Data!, protectedView.Length, result);
    }
}

bool TryParseUInt(Span<readonly(char)> text, uint* result) nothrow
=> text.TryParseUInt(result);

extension Span<readonly(char)>
{
    public immutable bool TryParseUInt(uint* result) nothrow
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::TryParseUInt("", 0, result);
        return Standard::Text::TryParseUInt(protectedView.Data!, protectedView.Length, result);
    }
}

bool TryParseLong(Span<readonly(char)> text, long* result) nothrow
=> text.TryParseLong(result);

extension Span<readonly(char)>
{
    public immutable bool TryParseLong(long* result) nothrow
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::TryParseLong("", 0, result);
        return Standard::Text::TryParseLong(protectedView.Data!, protectedView.Length, result);
    }
}

bool TryParseULong(Span<readonly(char)> text, ulong* result) nothrow
=> text.TryParseULong(result);

extension Span<readonly(char)>
{
    public immutable bool TryParseULong(ulong* result) nothrow
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::TryParseULong("", 0, result);
        return Standard::Text::TryParseULong(protectedView.Data!, protectedView.Length, result);
    }
}

bool TryParseExtraLong(Span<readonly(char)> text, extralong* result) nothrow
=> text.TryParseExtraLong(result);

extension Span<readonly(char)>
{
    public immutable bool TryParseExtraLong(extralong* result) nothrow
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::TryParseExtraLong("", 0, result);
        return Standard::Text::TryParseExtraLong(protectedView.Data!, protectedView.Length, result);
    }
}

bool TryParseNInt(Span<readonly(char)> text, nint* result) nothrow
=> text.TryParseNInt(result);

extension Span<readonly(char)>
{
    public immutable bool TryParseNInt(nint* result) nothrow
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::TryParseNInt("", 0, result);
        return Standard::Text::TryParseNInt(protectedView.Data!, protectedView.Length, result);
    }
}

bool TryParseNUInt(Span<readonly(char)> text, nuint* result) nothrow
=> text.TryParseNUInt(result);

extension Span<readonly(char)>
{
    public immutable bool TryParseNUInt(nuint* result) nothrow
    {
        Span<readonly(char)> protectedView = *this;
        if (protectedView.IsEmpty) return Standard::Text::TryParseNUInt("", 0, result);
        return Standard::Text::TryParseNUInt(protectedView.Data!, protectedView.Length, result);
    }
}

