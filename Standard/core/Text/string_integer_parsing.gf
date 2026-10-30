using Standard::Text;
namespace Standard::Text;

// Borrow counted String bytes, including embedded nulls; no copy or allocation.
byte ParseByte(ref immutable(String) text) => Standard::Text::ParseByte(text.Data, text.Length);

extension String
{
    public immutable byte ParseByte()
    => Standard::Text::ParseByte(ref *this);
}
bool TryParseByte(ref immutable(String) text, byte* result) nothrow
    => Standard::Text::TryParseByte(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseByte(byte* result) nothrow
    => Standard::Text::TryParseByte(ref *this, result);
}

sbyte ParseSByte(ref immutable(String) text) => Standard::Text::ParseSByte(text.Data, text.Length);

extension String
{
    public immutable sbyte ParseSByte()
    => Standard::Text::ParseSByte(ref *this);
}
bool TryParseSByte(ref immutable(String) text, sbyte* result) nothrow
    => Standard::Text::TryParseSByte(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseSByte(sbyte* result) nothrow
    => Standard::Text::TryParseSByte(ref *this, result);
}

short ParseShort(ref immutable(String) text) => Standard::Text::ParseShort(text.Data, text.Length);

extension String
{
    public immutable short ParseShort()
    => Standard::Text::ParseShort(ref *this);
}
bool TryParseShort(ref immutable(String) text, short* result) nothrow
    => Standard::Text::TryParseShort(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseShort(short* result) nothrow
    => Standard::Text::TryParseShort(ref *this, result);
}

ushort ParseUShort(ref immutable(String) text) => Standard::Text::ParseUShort(text.Data, text.Length);

extension String
{
    public immutable ushort ParseUShort()
    => Standard::Text::ParseUShort(ref *this);
}
bool TryParseUShort(ref immutable(String) text, ushort* result) nothrow
    => Standard::Text::TryParseUShort(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseUShort(ushort* result) nothrow
    => Standard::Text::TryParseUShort(ref *this, result);
}

uint ParseUInt(ref immutable(String) text) => Standard::Text::ParseUInt(text.Data, text.Length);

extension String
{
    public immutable uint ParseUInt()
    => Standard::Text::ParseUInt(ref *this);
}
bool TryParseUInt(ref immutable(String) text, uint* result) nothrow
    => Standard::Text::TryParseUInt(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseUInt(uint* result) nothrow
    => Standard::Text::TryParseUInt(ref *this, result);
}

long ParseLong(ref immutable(String) text) => Standard::Text::ParseLong(text.Data, text.Length);

extension String
{
    public immutable long ParseLong()
    => Standard::Text::ParseLong(ref *this);
}
bool TryParseLong(ref immutable(String) text, long* result) nothrow
    => Standard::Text::TryParseLong(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseLong(long* result) nothrow
    => Standard::Text::TryParseLong(ref *this, result);
}

ulong ParseULong(ref immutable(String) text) => Standard::Text::ParseULong(text.Data, text.Length);

extension String
{
    public immutable ulong ParseULong()
    => Standard::Text::ParseULong(ref *this);
}
bool TryParseULong(ref immutable(String) text, ulong* result) nothrow
    => Standard::Text::TryParseULong(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseULong(ulong* result) nothrow
    => Standard::Text::TryParseULong(ref *this, result);
}

extralong ParseExtraLong(ref immutable(String) text) => Standard::Text::ParseExtraLong(text.Data, text.Length);

extension String
{
    public immutable extralong ParseExtraLong()
    => Standard::Text::ParseExtraLong(ref *this);
}
bool TryParseExtraLong(ref immutable(String) text, extralong* result) nothrow
    => Standard::Text::TryParseExtraLong(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseExtraLong(extralong* result) nothrow
    => Standard::Text::TryParseExtraLong(ref *this, result);
}

nint ParseNInt(ref immutable(String) text) => Standard::Text::ParseNInt(text.Data, text.Length);

extension String
{
    public immutable nint ParseNInt()
    => Standard::Text::ParseNInt(ref *this);
}
bool TryParseNInt(ref immutable(String) text, nint* result) nothrow
    => Standard::Text::TryParseNInt(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseNInt(nint* result) nothrow
    => Standard::Text::TryParseNInt(ref *this, result);
}

nuint ParseNUInt(ref immutable(String) text) => Standard::Text::ParseNUInt(text.Data, text.Length);

extension String
{
    public immutable nuint ParseNUInt()
    => Standard::Text::ParseNUInt(ref *this);
}
bool TryParseNUInt(ref immutable(String) text, nuint* result) nothrow
    => Standard::Text::TryParseNUInt(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseNUInt(nuint* result) nothrow
    => Standard::Text::TryParseNUInt(ref *this, result);
}

