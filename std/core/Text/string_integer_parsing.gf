using std::Text;
namespace std::Text;

// Borrow counted String bytes, including embedded nulls; no copy or allocation.
byte ParseByte(ref immutable(String) text) => std::Text::ParseByte(text.Data, text.Length);

extension String
{
    public immutable byte ParseByte()
    => std::Text::ParseByte(ref *this);
}
bool TryParseByte(ref immutable(String) text, byte* result) nothrow
    => std::Text::TryParseByte(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseByte(byte* result) nothrow
    => std::Text::TryParseByte(ref *this, result);
}

sbyte ParseSByte(ref immutable(String) text) => std::Text::ParseSByte(text.Data, text.Length);

extension String
{
    public immutable sbyte ParseSByte()
    => std::Text::ParseSByte(ref *this);
}
bool TryParseSByte(ref immutable(String) text, sbyte* result) nothrow
    => std::Text::TryParseSByte(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseSByte(sbyte* result) nothrow
    => std::Text::TryParseSByte(ref *this, result);
}

short ParseShort(ref immutable(String) text) => std::Text::ParseShort(text.Data, text.Length);

extension String
{
    public immutable short ParseShort()
    => std::Text::ParseShort(ref *this);
}
bool TryParseShort(ref immutable(String) text, short* result) nothrow
    => std::Text::TryParseShort(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseShort(short* result) nothrow
    => std::Text::TryParseShort(ref *this, result);
}

ushort ParseUShort(ref immutable(String) text) => std::Text::ParseUShort(text.Data, text.Length);

extension String
{
    public immutable ushort ParseUShort()
    => std::Text::ParseUShort(ref *this);
}
bool TryParseUShort(ref immutable(String) text, ushort* result) nothrow
    => std::Text::TryParseUShort(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseUShort(ushort* result) nothrow
    => std::Text::TryParseUShort(ref *this, result);
}

uint ParseUInt(ref immutable(String) text) => std::Text::ParseUInt(text.Data, text.Length);

extension String
{
    public immutable uint ParseUInt()
    => std::Text::ParseUInt(ref *this);
}
bool TryParseUInt(ref immutable(String) text, uint* result) nothrow
    => std::Text::TryParseUInt(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseUInt(uint* result) nothrow
    => std::Text::TryParseUInt(ref *this, result);
}

long ParseLong(ref immutable(String) text) => std::Text::ParseLong(text.Data, text.Length);

extension String
{
    public immutable long ParseLong()
    => std::Text::ParseLong(ref *this);
}
bool TryParseLong(ref immutable(String) text, long* result) nothrow
    => std::Text::TryParseLong(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseLong(long* result) nothrow
    => std::Text::TryParseLong(ref *this, result);
}

ulong ParseULong(ref immutable(String) text) => std::Text::ParseULong(text.Data, text.Length);

extension String
{
    public immutable ulong ParseULong()
    => std::Text::ParseULong(ref *this);
}
bool TryParseULong(ref immutable(String) text, ulong* result) nothrow
    => std::Text::TryParseULong(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseULong(ulong* result) nothrow
    => std::Text::TryParseULong(ref *this, result);
}

extralong ParseExtraLong(ref immutable(String) text) => std::Text::ParseExtraLong(text.Data, text.Length);

extension String
{
    public immutable extralong ParseExtraLong()
    => std::Text::ParseExtraLong(ref *this);
}
bool TryParseExtraLong(ref immutable(String) text, extralong* result) nothrow
    => std::Text::TryParseExtraLong(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseExtraLong(extralong* result) nothrow
    => std::Text::TryParseExtraLong(ref *this, result);
}

nint ParseNInt(ref immutable(String) text) => std::Text::ParseNInt(text.Data, text.Length);

extension String
{
    public immutable nint ParseNInt()
    => std::Text::ParseNInt(ref *this);
}
bool TryParseNInt(ref immutable(String) text, nint* result) nothrow
    => std::Text::TryParseNInt(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseNInt(nint* result) nothrow
    => std::Text::TryParseNInt(ref *this, result);
}

nuint ParseNUInt(ref immutable(String) text) => std::Text::ParseNUInt(text.Data, text.Length);

extension String
{
    public immutable nuint ParseNUInt()
    => std::Text::ParseNUInt(ref *this);
}
bool TryParseNUInt(ref immutable(String) text, nuint* result) nothrow
    => std::Text::TryParseNUInt(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseNUInt(nuint* result) nothrow
    => std::Text::TryParseNUInt(ref *this, result);
}

