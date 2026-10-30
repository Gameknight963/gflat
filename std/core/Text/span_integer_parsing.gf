using std::Memory;
namespace std::Text;
bool TryParseByte(Span<readonly(char)> text, byte* result) nothrow
=> text.TryParseByte(result);

extension Span<readonly(char)>
{
    public bool TryParseByte(byte* result) nothrow
    {
        if ((*this).IsEmpty) return std::Text::TryParseByte("", 0, result);
        return std::Text::TryParseByte((*this).Data!, (*this).Length, result);
    }
}

bool TryParseSByte(Span<readonly(char)> text, sbyte* result) nothrow
=> text.TryParseSByte(result);

extension Span<readonly(char)>
{
    public bool TryParseSByte(sbyte* result) nothrow
    {
        if ((*this).IsEmpty) return std::Text::TryParseSByte("", 0, result);
        return std::Text::TryParseSByte((*this).Data!, (*this).Length, result);
    }
}

bool TryParseShort(Span<readonly(char)> text, short* result) nothrow
=> text.TryParseShort(result);

extension Span<readonly(char)>
{
    public bool TryParseShort(short* result) nothrow
    {
        if ((*this).IsEmpty) return std::Text::TryParseShort("", 0, result);
        return std::Text::TryParseShort((*this).Data!, (*this).Length, result);
    }
}

bool TryParseUShort(Span<readonly(char)> text, ushort* result) nothrow
=> text.TryParseUShort(result);

extension Span<readonly(char)>
{
    public bool TryParseUShort(ushort* result) nothrow
    {
        if ((*this).IsEmpty) return std::Text::TryParseUShort("", 0, result);
        return std::Text::TryParseUShort((*this).Data!, (*this).Length, result);
    }
}

bool TryParseInt(Span<readonly(char)> text, int* result) nothrow
=> text.TryParseInt(result);

extension Span<readonly(char)>
{
    public bool TryParseInt(int* result) nothrow
    {
        if ((*this).IsEmpty) return std::Text::TryParseInt("", 0, result);
        return std::Text::TryParseInt((*this).Data!, (*this).Length, result);
    }
}

bool TryParseUInt(Span<readonly(char)> text, uint* result) nothrow
=> text.TryParseUInt(result);

extension Span<readonly(char)>
{
    public bool TryParseUInt(uint* result) nothrow
    {
        if ((*this).IsEmpty) return std::Text::TryParseUInt("", 0, result);
        return std::Text::TryParseUInt((*this).Data!, (*this).Length, result);
    }
}

bool TryParseLong(Span<readonly(char)> text, long* result) nothrow
=> text.TryParseLong(result);

extension Span<readonly(char)>
{
    public bool TryParseLong(long* result) nothrow
    {
        if ((*this).IsEmpty) return std::Text::TryParseLong("", 0, result);
        return std::Text::TryParseLong((*this).Data!, (*this).Length, result);
    }
}

bool TryParseULong(Span<readonly(char)> text, ulong* result) nothrow
=> text.TryParseULong(result);

extension Span<readonly(char)>
{
    public bool TryParseULong(ulong* result) nothrow
    {
        if ((*this).IsEmpty) return std::Text::TryParseULong("", 0, result);
        return std::Text::TryParseULong((*this).Data!, (*this).Length, result);
    }
}

bool TryParseExtraLong(Span<readonly(char)> text, extralong* result) nothrow
=> text.TryParseExtraLong(result);

extension Span<readonly(char)>
{
    public bool TryParseExtraLong(extralong* result) nothrow
    {
        if ((*this).IsEmpty) return std::Text::TryParseExtraLong("", 0, result);
        return std::Text::TryParseExtraLong((*this).Data!, (*this).Length, result);
    }
}

bool TryParseNInt(Span<readonly(char)> text, nint* result) nothrow
=> text.TryParseNInt(result);

extension Span<readonly(char)>
{
    public bool TryParseNInt(nint* result) nothrow
    {
        if ((*this).IsEmpty) return std::Text::TryParseNInt("", 0, result);
        return std::Text::TryParseNInt((*this).Data!, (*this).Length, result);
    }
}

bool TryParseNUInt(Span<readonly(char)> text, nuint* result) nothrow
=> text.TryParseNUInt(result);

extension Span<readonly(char)>
{
    public bool TryParseNUInt(nuint* result) nothrow
    {
        if ((*this).IsEmpty) return std::Text::TryParseNUInt("", 0, result);
        return std::Text::TryParseNUInt((*this).Data!, (*this).Length, result);
    }
}

