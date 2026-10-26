namespace std::Text;

namespace integer_parsing_detail
{
    // Check the bound before multiplication, so even ULong overflow is defined.
    bool Magnitude(readonly(char)* text, nuint count, ulong positiveLimit,
        ulong negativeLimit, bool signedInput, ulong* magnitude, bool* negative) nothrow
    {
        *magnitude = 0ul;
        *negative = false;
        if (count == 0) return false;
        nuint index = 0;
        bool minus = text[0] == '-';
        if (minus && !signedInput) return false;
        if (minus || text[0] == '+') index++;
        if (index == count) return false;
        ulong limit = positiveLimit;
        if (minus) limit = negativeLimit;
        ulong value = 0ul;
        for (; index < count; index++)
        {
            char ch = text[index];
            if (ch < '0' || ch > '9') return false;
            ulong digit = (ulong)(ch - '0');
            if (value > (limit - digit) / 10ul) return false;
            value = value * 10ul + digit;
        }
        *magnitude = value;
        *negative = minus;
        return true;
    }

    nuint Length(readonly(char)* text) nothrow
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        return count;
    }
}

// Strict decimal parsing: no whitespace, separators, radix prefixes, or trailing bytes.
// Unsigned parsers reject a minus sign, including -0. Every failure writes zero.
bool TryParseByte(readonly(char)* text, nuint count, byte* result) nothrow
{
    ulong magnitude = 0ul;
    bool negative = false;
    if (!integer_parsing_detail::Magnitude(text, count, 255ul, 0ul, false, &magnitude, &negative)) { *result = (byte)0; return false; }
    *result = (byte)magnitude;
    return true;
}

bool TryParseByte(readonly(char)* text, byte* result) nothrow
    => TryParseByte(text, integer_parsing_detail::Length(text), result);

byte ParseByte(readonly(char)* text, nuint count)
{
    byte result = (byte)0;
    if (!TryParseByte(text, count, &result))
        throw new Exception("Invalid or out-of-range decimal byte");
    return result;
}

byte ParseByte(readonly(char)* text)
    => ParseByte(text, integer_parsing_detail::Length(text));

bool TryParseSByte(readonly(char)* text, nuint count, sbyte* result) nothrow
{
    ulong magnitude = 0ul;
    bool negative = false;
    if (!integer_parsing_detail::Magnitude(text, count, 127ul, 128ul, true, &magnitude, &negative)) { *result = (sbyte)0; return false; }
    if (negative) *result = (sbyte)(0ul - magnitude);
    else *result = (sbyte)magnitude;
    return true;
}

bool TryParseSByte(readonly(char)* text, sbyte* result) nothrow
    => TryParseSByte(text, integer_parsing_detail::Length(text), result);

sbyte ParseSByte(readonly(char)* text, nuint count)
{
    sbyte result = (sbyte)0;
    if (!TryParseSByte(text, count, &result))
        throw new Exception("Invalid or out-of-range decimal sbyte");
    return result;
}

sbyte ParseSByte(readonly(char)* text)
    => ParseSByte(text, integer_parsing_detail::Length(text));

bool TryParseShort(readonly(char)* text, nuint count, short* result) nothrow
{
    ulong magnitude = 0ul;
    bool negative = false;
    if (!integer_parsing_detail::Magnitude(text, count, 32767ul, 32768ul, true, &magnitude, &negative)) { *result = (short)0; return false; }
    if (negative) *result = (short)(0ul - magnitude);
    else *result = (short)magnitude;
    return true;
}

bool TryParseShort(readonly(char)* text, short* result) nothrow
    => TryParseShort(text, integer_parsing_detail::Length(text), result);

short ParseShort(readonly(char)* text, nuint count)
{
    short result = (short)0;
    if (!TryParseShort(text, count, &result))
        throw new Exception("Invalid or out-of-range decimal short");
    return result;
}

short ParseShort(readonly(char)* text)
    => ParseShort(text, integer_parsing_detail::Length(text));

bool TryParseUShort(readonly(char)* text, nuint count, ushort* result) nothrow
{
    ulong magnitude = 0ul;
    bool negative = false;
    if (!integer_parsing_detail::Magnitude(text, count, 65535ul, 0ul, false, &magnitude, &negative)) { *result = (ushort)0; return false; }
    *result = (ushort)magnitude;
    return true;
}

bool TryParseUShort(readonly(char)* text, ushort* result) nothrow
    => TryParseUShort(text, integer_parsing_detail::Length(text), result);

ushort ParseUShort(readonly(char)* text, nuint count)
{
    ushort result = (ushort)0;
    if (!TryParseUShort(text, count, &result))
        throw new Exception("Invalid or out-of-range decimal ushort");
    return result;
}

ushort ParseUShort(readonly(char)* text)
    => ParseUShort(text, integer_parsing_detail::Length(text));

bool TryParseUInt(readonly(char)* text, nuint count, uint* result) nothrow
{
    ulong magnitude = 0ul;
    bool negative = false;
    if (!integer_parsing_detail::Magnitude(text, count, 4294967295ul, 0ul, false, &magnitude, &negative)) { *result = (uint)0; return false; }
    *result = (uint)magnitude;
    return true;
}

bool TryParseUInt(readonly(char)* text, uint* result) nothrow
    => TryParseUInt(text, integer_parsing_detail::Length(text), result);

uint ParseUInt(readonly(char)* text, nuint count)
{
    uint result = (uint)0;
    if (!TryParseUInt(text, count, &result))
        throw new Exception("Invalid or out-of-range decimal uint");
    return result;
}

uint ParseUInt(readonly(char)* text)
    => ParseUInt(text, integer_parsing_detail::Length(text));

bool TryParseLong(readonly(char)* text, nuint count, long* result) nothrow
{
    ulong magnitude = 0ul;
    bool negative = false;
    if (!integer_parsing_detail::Magnitude(text, count, 9223372036854775807ul, 9223372036854775808ul, true, &magnitude, &negative)) { *result = (long)0; return false; }
    if (negative) *result = (long)(0ul - magnitude);
    else *result = (long)magnitude;
    return true;
}

bool TryParseLong(readonly(char)* text, long* result) nothrow
    => TryParseLong(text, integer_parsing_detail::Length(text), result);

long ParseLong(readonly(char)* text, nuint count)
{
    long result = (long)0;
    if (!TryParseLong(text, count, &result))
        throw new Exception("Invalid or out-of-range decimal long");
    return result;
}

long ParseLong(readonly(char)* text)
    => ParseLong(text, integer_parsing_detail::Length(text));

bool TryParseULong(readonly(char)* text, nuint count, ulong* result) nothrow
{
    ulong magnitude = 0ul;
    bool negative = false;
    if (!integer_parsing_detail::Magnitude(text, count, 18446744073709551615ul, 0ul, false, &magnitude, &negative)) { *result = (ulong)0; return false; }
    *result = (ulong)magnitude;
    return true;
}

bool TryParseULong(readonly(char)* text, ulong* result) nothrow
    => TryParseULong(text, integer_parsing_detail::Length(text), result);

ulong ParseULong(readonly(char)* text, nuint count)
{
    ulong result = (ulong)0;
    if (!TryParseULong(text, count, &result))
        throw new Exception("Invalid or out-of-range decimal ulong");
    return result;
}

ulong ParseULong(readonly(char)* text)
    => ParseULong(text, integer_parsing_detail::Length(text));

bool TryParseExtraLong(readonly(char)* text, nuint count, extralong* result) nothrow
{
    if (count == 0) { *result = (extralong)0; return false; }
    nuint index = 0;
    bool negative = text[0] == '-';
    if (negative || text[0] == '+') index++;
    if (index == count) { *result = (extralong)0; return false; }
    // -floor(2^127 / 10), assembled without 128-bit division or wide literals.
    extralong cutoff = -(((extralong)922337203685477580ul << 64) | (extralong)14757395258967641292ul);
    int last = 7;
    if (negative) last = 8;
    extralong value = (extralong)0;
    for (; index < count; index++)
    {
        char ch = text[index];
        if (ch < '0' || ch > '9') { *result = (extralong)0; return false; }
        int digit = ch - '0';
        if (value < cutoff || (value == cutoff && digit > last))
        { *result = (extralong)0; return false; }
        value = value * (extralong)10 - (extralong)digit;
    }
    if (!negative) value = -value;
    *result = value;
    return true;
}

bool TryParseExtraLong(readonly(char)* text, extralong* result) nothrow
    => TryParseExtraLong(text, integer_parsing_detail::Length(text), result);

extralong ParseExtraLong(readonly(char)* text, nuint count)
{
    extralong result = (extralong)0;
    if (!TryParseExtraLong(text, count, &result))
        throw new Exception("Invalid or out-of-range decimal extralong");
    return result;
}

extralong ParseExtraLong(readonly(char)* text)
    => ParseExtraLong(text, integer_parsing_detail::Length(text));

bool TryParseNInt(readonly(char)* text, nuint count, nint* result) nothrow
{
    ulong negativeLimit = 1ul << (sizeof(nint) * 8 - 1);
    ulong positiveLimit = negativeLimit - 1ul;
    ulong magnitude = 0ul;
    bool negative = false;
    if (!integer_parsing_detail::Magnitude(text, count, positiveLimit, negativeLimit, true, &magnitude, &negative)) { *result = (nint)0; return false; }
    if (negative) *result = (nint)(0ul - magnitude);
    else *result = (nint)magnitude;
    return true;
}

bool TryParseNInt(readonly(char)* text, nint* result) nothrow
    => TryParseNInt(text, integer_parsing_detail::Length(text), result);

nint ParseNInt(readonly(char)* text, nuint count)
{
    nint result = (nint)0;
    if (!TryParseNInt(text, count, &result))
        throw new Exception("Invalid or out-of-range decimal nint");
    return result;
}

nint ParseNInt(readonly(char)* text)
    => ParseNInt(text, integer_parsing_detail::Length(text));

bool TryParseNUInt(readonly(char)* text, nuint count, nuint* result) nothrow
{
    ulong magnitude = 0ul;
    bool negative = false;
    if (!integer_parsing_detail::Magnitude(text, count, (ulong)((nuint)0 - (nuint)1), 0ul, false, &magnitude, &negative)) { *result = (nuint)0; return false; }
    *result = (nuint)magnitude;
    return true;
}

bool TryParseNUInt(readonly(char)* text, nuint* result) nothrow
    => TryParseNUInt(text, integer_parsing_detail::Length(text), result);

nuint ParseNUInt(readonly(char)* text, nuint count)
{
    nuint result = (nuint)0;
    if (!TryParseNUInt(text, count, &result))
        throw new Exception("Invalid or out-of-range decimal nuint");
    return result;
}

nuint ParseNUInt(readonly(char)* text)
    => ParseNUInt(text, integer_parsing_detail::Length(text));

