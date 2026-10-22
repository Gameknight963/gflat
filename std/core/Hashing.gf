namespace std::Hashing;

// Deterministic FNV-1a over counted bytes, including embedded nulls.
// Not suitable for cryptography or keys supplied by hostile users.
nuint Bytes(readonly(char)* data, nuint count) nothrow
{
    uint hash = (uint)2166136261;
    for (nuint i = 0; i < count; i++)
        hash = (hash ^ (uint)(byte)data[i]) * (uint)16777619;
    return (nuint)hash;
}

// Mix all 64 bits before folding into a target-independent 32-bit result.
nuint Integer(ulong value) nothrow
{
    value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9ul;
    value = (value ^ (value >> 27)) * 0x94d049bb133111ebul;
    value = value ^ (value >> 31);
    return (nuint)((uint)value ^ (uint)(value >> 32));
}
