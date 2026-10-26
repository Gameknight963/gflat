using std::Text;
namespace std::Text;

// Borrow counted String bytes, including embedded nulls; no copy or allocation.
byte ParseByte(this ref immutable(String) text) => std::Text::ParseByte(text.Data, text.Length);
bool TryParseByte(this ref immutable(String) text, byte* result) nothrow
    => std::Text::TryParseByte(text.Data, text.Length, result);

sbyte ParseSByte(this ref immutable(String) text) => std::Text::ParseSByte(text.Data, text.Length);
bool TryParseSByte(this ref immutable(String) text, sbyte* result) nothrow
    => std::Text::TryParseSByte(text.Data, text.Length, result);

short ParseShort(this ref immutable(String) text) => std::Text::ParseShort(text.Data, text.Length);
bool TryParseShort(this ref immutable(String) text, short* result) nothrow
    => std::Text::TryParseShort(text.Data, text.Length, result);

ushort ParseUShort(this ref immutable(String) text) => std::Text::ParseUShort(text.Data, text.Length);
bool TryParseUShort(this ref immutable(String) text, ushort* result) nothrow
    => std::Text::TryParseUShort(text.Data, text.Length, result);

uint ParseUInt(this ref immutable(String) text) => std::Text::ParseUInt(text.Data, text.Length);
bool TryParseUInt(this ref immutable(String) text, uint* result) nothrow
    => std::Text::TryParseUInt(text.Data, text.Length, result);

long ParseLong(this ref immutable(String) text) => std::Text::ParseLong(text.Data, text.Length);
bool TryParseLong(this ref immutable(String) text, long* result) nothrow
    => std::Text::TryParseLong(text.Data, text.Length, result);

ulong ParseULong(this ref immutable(String) text) => std::Text::ParseULong(text.Data, text.Length);
bool TryParseULong(this ref immutable(String) text, ulong* result) nothrow
    => std::Text::TryParseULong(text.Data, text.Length, result);

extralong ParseExtraLong(this ref immutable(String) text) => std::Text::ParseExtraLong(text.Data, text.Length);
bool TryParseExtraLong(this ref immutable(String) text, extralong* result) nothrow
    => std::Text::TryParseExtraLong(text.Data, text.Length, result);

nint ParseNInt(this ref immutable(String) text) => std::Text::ParseNInt(text.Data, text.Length);
bool TryParseNInt(this ref immutable(String) text, nint* result) nothrow
    => std::Text::TryParseNInt(text.Data, text.Length, result);

nuint ParseNUInt(this ref immutable(String) text) => std::Text::ParseNUInt(text.Data, text.Length);
bool TryParseNUInt(this ref immutable(String) text, nuint* result) nothrow
    => std::Text::TryParseNUInt(text.Data, text.Length, result);

