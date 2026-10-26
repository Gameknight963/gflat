namespace std::Text;

// Optional throwing wrappers; TryParseHex... alone needs no exception runtime.

byte ParseHexByte(readonly(char)* text, nuint count)
{
    byte result = (byte)0;
    if (!TryParseHexByte(text, count, &result))
        throw new Exception("Invalid or out-of-range hexadecimal byte");
    return result;
}

byte ParseHexByte(readonly(char)* text)
    => ParseHexByte(text, hex_integer_parsing_detail::Length(text));

ushort ParseHexUShort(readonly(char)* text, nuint count)
{
    ushort result = (ushort)0;
    if (!TryParseHexUShort(text, count, &result))
        throw new Exception("Invalid or out-of-range hexadecimal ushort");
    return result;
}

ushort ParseHexUShort(readonly(char)* text)
    => ParseHexUShort(text, hex_integer_parsing_detail::Length(text));

uint ParseHexUInt(readonly(char)* text, nuint count)
{
    uint result = (uint)0;
    if (!TryParseHexUInt(text, count, &result))
        throw new Exception("Invalid or out-of-range hexadecimal uint");
    return result;
}

uint ParseHexUInt(readonly(char)* text)
    => ParseHexUInt(text, hex_integer_parsing_detail::Length(text));

ulong ParseHexULong(readonly(char)* text, nuint count)
{
    ulong result = (ulong)0;
    if (!TryParseHexULong(text, count, &result))
        throw new Exception("Invalid or out-of-range hexadecimal ulong");
    return result;
}

ulong ParseHexULong(readonly(char)* text)
    => ParseHexULong(text, hex_integer_parsing_detail::Length(text));

nuint ParseHexNUInt(readonly(char)* text, nuint count)
{
    nuint result = (nuint)0;
    if (!TryParseHexNUInt(text, count, &result))
        throw new Exception("Invalid or out-of-range hexadecimal nuint");
    return result;
}

nuint ParseHexNUInt(readonly(char)* text)
    => ParseHexNUInt(text, hex_integer_parsing_detail::Length(text));

