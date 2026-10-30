namespace Standard::Text::Utf8;

bool IsValidScalar(uint scalar) nothrow
    => scalar <= 0x10ffffu && !(scalar >= 0xd800u && scalar <= 0xdfffu);

// Zero means the input is not a Unicode scalar value.
nuint EncodedLength(uint scalar) nothrow
{
    if (!IsValidScalar(scalar)) return 0;
    if (scalar < 0x80u) return 1;
    if (scalar < 0x800u) return 2;
    if (scalar < 0x10000u) return 3;
    return 4;
}

// Decode one complete scalar. On failure both outputs are zero; no bytes consumed.
// The input is counted and may contain U+0000. No replacement characters are emitted.
// Outputs must not overlap each other; either may overlap input storage.
bool TryDecode(readonly(char)* input, nuint count, uint* scalar, nuint* consumed) nothrow
{
    if (count == 0) return DecodeFailure(scalar, consumed);
    uint first = (uint)(byte)input[0];
    if (first < 0x80u) { *scalar = first; *consumed = 1; return true; }
    nuint required = 0;
    uint value = 0u;
    uint minimum = 0u;
    if (first >= 0xc2u && first <= 0xdfu)
    {
        required = 2; value = first & 0x1fu; minimum = 0x80u;
    }
    else if (first >= 0xe0u && first <= 0xefu)
    {
        required = 3; value = first & 0x0fu; minimum = 0x800u;
    }
    else if (first >= 0xf0u && first <= 0xf4u)
    {
        required = 4; value = first & 0x07u; minimum = 0x10000u;
    }
    else return DecodeFailure(scalar, consumed);
    if (count < required) return DecodeFailure(scalar, consumed);
    for (nuint i = 1; i < required; i++)
    {
        uint next = (uint)(byte)input[i];
        if (next < 0x80u || next > 0xbfu) return DecodeFailure(scalar, consumed);
        value = (value << 6) | (next & 0x3fu);
    }
    if (value < minimum || !IsValidScalar(value)) return DecodeFailure(scalar, consumed);
    *scalar = value;
    *consumed = required;
    return true;
}

// Decode only the final scalar; preceding bytes are not validated.
// Outputs must be disjoint from each other but may overlap input storage.
// All required input reads precede result stores; failure clears both outputs.
bool TryDecodeLast(readonly(char)* input, nuint count, uint* scalar, nuint* consumed) nothrow
{
    if (count == 0) return DecodeFailure(scalar, consumed);
    nuint start = count - 1;
    while (start > 0 && count - start < 4)
    {
        uint next = (uint)(byte)input[start];
        if (next < 0x80u || next > 0xbfu) break;
        start--;
    }
    uint value = 0u;
    nuint length = 0;
    if (!TryDecode(input + start, count - start, &value, &length) || length != count - start)
        return DecodeFailure(scalar, consumed);
    *scalar = value;
    *consumed = length;
    return true;
}

// Delay every output store until the bytes needed for the result were examined.
// Scalar/count outputs may borrow input storage, but must not overlap each other.
private bool DecodeFailure(uint* scalar, nuint* consumed) nothrow
{
    *scalar = 0u; *consumed = 0;
    return false;
}

// Preflight before writing: invalid scalars and insufficient capacity leave output untouched.
// The result is counted bytes, without a null terminator.
bool TryEncode(uint scalar, char* output, nuint capacity, nuint* written) nothrow
{
    *written = 0;
    nuint required = EncodedLength(scalar);
    if (required == 0 || capacity < required) return false;
    if (required == 1) output[0] = (char)scalar;
    else if (required == 2)
    {
        output[0] = (char)(0xc0u | (scalar >> 6));
        output[1] = (char)(0x80u | (scalar & 0x3fu));
    }
    else if (required == 3)
    {
        output[0] = (char)(0xe0u | (scalar >> 12));
        output[1] = (char)(0x80u | ((scalar >> 6) & 0x3fu));
        output[2] = (char)(0x80u | (scalar & 0x3fu));
    }
    else
    {
        output[0] = (char)(0xf0u | (scalar >> 18));
        output[1] = (char)(0x80u | ((scalar >> 12) & 0x3fu));
        output[2] = (char)(0x80u | ((scalar >> 6) & 0x3fu));
        output[3] = (char)(0x80u | (scalar & 0x3fu));
    }
    *written = required;
    return true;
}

// Count Unicode scalar values, not bytes or grapheme clusters. Failure writes zero.
// The count output may overlap input; bytes are examined before storing its result.
bool TryCount(readonly(char)* input, nuint count, nuint* scalarCount) nothrow
{
    nuint offset = 0;
    nuint result = 0;
    while (offset < count)
    {
        uint scalar = 0u;
        nuint consumed = 0;
        if (!TryDecode(input + offset, count - offset, &scalar, &consumed))
        {
            *scalarCount = 0; return false;
        }
        offset += consumed;
        result++;
    }
    *scalarCount = result;
    return true;
}

bool IsValid(readonly(char)* input, nuint count) nothrow
{
    nuint scalars = 0;
    return TryCount(input, count, &scalars);
}
