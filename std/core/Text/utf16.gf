namespace std::Text::Utf16;

// ushort values are host code units, not an endian-specific serialized byte stream.
bool IsValidScalar(uint scalar) nothrow
    => scalar <= 0x10ffffu && !(scalar >= 0xd800u && scalar <= 0xdfffu);

// Zero means invalid scalar; lengths and capacities count ushort code units.
nuint EncodedLength(uint scalar) nothrow
{
    if (!IsValidScalar(scalar)) return 0;
    if (scalar < 0x10000u) return 1;
    return 2;
}

// Strict decoding: isolated/truncated surrogates fail, clearing both outputs.
// Outputs must be disjoint from each other but may overlap input storage.
bool TryDecode(readonly(ushort)* input, nuint count, uint* scalar, nuint* consumed) nothrow
{
    if (count == 0) return DecodeFailure(scalar, consumed);
    uint first = (uint)input[0];
    if (first < 0xd800u || first > 0xdfffu)
    {
        *scalar = first; *consumed = 1; return true;
    }
    if (first > 0xdbffu || count < 2) return DecodeFailure(scalar, consumed);
    uint second = (uint)input[1];
    if (second < 0xdc00u || second > 0xdfffu) return DecodeFailure(scalar, consumed);
    uint value = 0x10000u + ((first - 0xd800u) << 10) + second - 0xdc00u;
    *scalar = value; *consumed = 2;
    return true;
}

// Decode only the final scalar; preceding code units are not validated.
// Outputs must be disjoint from each other but may overlap input storage.
// All required input reads precede result stores; failure clears both outputs.
bool TryDecodeLast(readonly(ushort)* input, nuint count, uint* scalar, nuint* consumed) nothrow
{
    if (count == 0) return DecodeFailure(scalar, consumed);
    nuint start = count - 1;
    uint last = (uint)input[start];
    if (last >= 0xdc00u && last <= 0xdfffu)
    {
        if (start == 0) return DecodeFailure(scalar, consumed);
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

private bool DecodeFailure(uint* scalar, nuint* consumed) nothrow
{
    *scalar = 0u; *consumed = 0; return false;
}

// Invalid scalars and insufficient capacity leave output untouched; written is zero.
// No terminator is appended. The written count must not overlap output storage.
bool TryEncode(uint scalar, ushort* output, nuint capacity, nuint* written) nothrow
{
    *written = 0;
    nuint required = EncodedLength(scalar);
    if (required == 0 || capacity < required) return false;
    if (required == 1) output[0] = (ushort)scalar;
    else
    {
        uint value = scalar - 0x10000u;
        output[0] = (ushort)(0xd800u + (value >> 10));
        output[1] = (ushort)(0xdc00u + (value & 0x3ffu));
    }
    *written = required;
    return true;
}

// Counts scalars, not code units or grapheme clusters. Failure writes zero.
// Count output may overlap input; its store follows all required input reads.
bool TryCount(readonly(ushort)* input, nuint count, nuint* scalarCount) nothrow
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

bool IsValid(readonly(ushort)* input, nuint count) nothrow
{
    nuint scalars = 0;
    return TryCount(input, count, &scalars);
}
