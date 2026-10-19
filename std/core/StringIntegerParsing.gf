namespace std;

// Borrow counted String bytes, including embedded nulls; no copy or allocation.
byte ParseByte(this ref readonly(String) text) => std::ParseByte(text.Data, text.Length);
bool TryParseByte(this ref readonly(String) text, byte* result) nothrow
    => std::TryParseByte(text.Data, text.Length, result);

sbyte ParseSByte(this ref readonly(String) text) => std::ParseSByte(text.Data, text.Length);
bool TryParseSByte(this ref readonly(String) text, sbyte* result) nothrow
    => std::TryParseSByte(text.Data, text.Length, result);

short ParseShort(this ref readonly(String) text) => std::ParseShort(text.Data, text.Length);
bool TryParseShort(this ref readonly(String) text, short* result) nothrow
    => std::TryParseShort(text.Data, text.Length, result);

ushort ParseUShort(this ref readonly(String) text) => std::ParseUShort(text.Data, text.Length);
bool TryParseUShort(this ref readonly(String) text, ushort* result) nothrow
    => std::TryParseUShort(text.Data, text.Length, result);

uint ParseUInt(this ref readonly(String) text) => std::ParseUInt(text.Data, text.Length);
bool TryParseUInt(this ref readonly(String) text, uint* result) nothrow
    => std::TryParseUInt(text.Data, text.Length, result);

long ParseLong(this ref readonly(String) text) => std::ParseLong(text.Data, text.Length);
bool TryParseLong(this ref readonly(String) text, long* result) nothrow
    => std::TryParseLong(text.Data, text.Length, result);

ulong ParseULong(this ref readonly(String) text) => std::ParseULong(text.Data, text.Length);
bool TryParseULong(this ref readonly(String) text, ulong* result) nothrow
    => std::TryParseULong(text.Data, text.Length, result);

nint ParseNInt(this ref readonly(String) text) => std::ParseNInt(text.Data, text.Length);
bool TryParseNInt(this ref readonly(String) text, nint* result) nothrow
    => std::TryParseNInt(text.Data, text.Length, result);

nuint ParseNUInt(this ref readonly(String) text) => std::ParseNUInt(text.Data, text.Length);
bool TryParseNUInt(this ref readonly(String) text, nuint* result) nothrow
    => std::TryParseNUInt(text.Data, text.Length, result);

