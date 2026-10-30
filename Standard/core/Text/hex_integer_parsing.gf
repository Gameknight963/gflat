namespace Standard::Text;

// Strict unsigned hexadecimal text, with an optional 0x/0X prefix.
// Signs, whitespace, separators, and empty digit sequences are rejected.
namespace hex_integer_parsing_detail
{
    bool Magnitude(readonly(char)* text, nuint count, ulong limit, ulong* result) nothrow
    {
        if (count == 0) return false;
        nuint index = 0;
        if (count >= 2 && text[0] == '0' && (text[1] == 'x' || text[1] == 'X')) index = 2;
        if (index == count) return false;
        ulong value = 0ul;
        for (; index < count; index++)
        {
            char character = text[index];
            ulong digit = 0ul;
            if (character >= '0' && character <= '9') digit = (ulong)(character - '0');
            else if (character >= 'a' && character <= 'f') digit = (ulong)(character - 'a') + 10ul;
            else if (character >= 'A' && character <= 'F') digit = (ulong)(character - 'A') + 10ul;
            else return false;
            if (value > (limit - digit) / 16ul) return false;
            value = value * 16ul + digit;
        }
        *result = value;
        return true;
    }

    nuint Length(readonly(char)* text) nothrow
    {
        nuint count = 0;
        while (text[count] != '\0') count++;
        return count;
    }
}

bool TryParseHexByte(readonly(char)* text, nuint count, byte* result) nothrow
{
    ulong magnitude = 0ul;
    if (!hex_integer_parsing_detail::Magnitude(text, count, 255ul, &magnitude)) { *result = (byte)0; return false; }
    *result = (byte)magnitude;
    return true;
}

bool TryParseHexByte(readonly(char)* text, byte* result) nothrow
    => TryParseHexByte(text, hex_integer_parsing_detail::Length(text), result);

bool TryParseHexUShort(readonly(char)* text, nuint count, ushort* result) nothrow
{
    ulong magnitude = 0ul;
    if (!hex_integer_parsing_detail::Magnitude(text, count, 65535ul, &magnitude)) { *result = (ushort)0; return false; }
    *result = (ushort)magnitude;
    return true;
}

bool TryParseHexUShort(readonly(char)* text, ushort* result) nothrow
    => TryParseHexUShort(text, hex_integer_parsing_detail::Length(text), result);

bool TryParseHexUInt(readonly(char)* text, nuint count, uint* result) nothrow
{
    ulong magnitude = 0ul;
    if (!hex_integer_parsing_detail::Magnitude(text, count, 4294967295ul, &magnitude)) { *result = (uint)0; return false; }
    *result = (uint)magnitude;
    return true;
}

bool TryParseHexUInt(readonly(char)* text, uint* result) nothrow
    => TryParseHexUInt(text, hex_integer_parsing_detail::Length(text), result);

bool TryParseHexULong(readonly(char)* text, nuint count, ulong* result) nothrow
{
    ulong magnitude = 0ul;
    if (!hex_integer_parsing_detail::Magnitude(text, count, 18446744073709551615ul, &magnitude)) { *result = (ulong)0; return false; }
    *result = (ulong)magnitude;
    return true;
}

bool TryParseHexULong(readonly(char)* text, ulong* result) nothrow
    => TryParseHexULong(text, hex_integer_parsing_detail::Length(text), result);

bool TryParseHexNUInt(readonly(char)* text, nuint count, nuint* result) nothrow
{
    ulong magnitude = 0ul;
    if (!hex_integer_parsing_detail::Magnitude(text, count, (ulong)((nuint)0 - (nuint)1), &magnitude)) { *result = (nuint)0; return false; }
    *result = (nuint)magnitude;
    return true;
}

bool TryParseHexNUInt(readonly(char)* text, nuint* result) nothrow
    => TryParseHexNUInt(text, hex_integer_parsing_detail::Length(text), result);

