namespace Standard::Hashing;

// Deterministic FNV-1a over counted bytes, including embedded nulls.
// Not suitable for cryptography or keys supplied by hostile users.
nuint Bytes(readonly(char)* data, nuint count) nothrow
{
    uint hash = (uint)2166136261;
    for (nuint i = 0; i < count; i++)
        hash = (hash ^ (uint)(byte)data[i]) * (uint)16777619;
    return (nuint)hash;
}

// Avalanche all 64 bits before combining or folding them.
private ulong Mix64(ulong value) nothrow
{
    value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9ul;
    value = (value ^ (value >> 27)) * 0x94d049bb133111ebul;
    value = value ^ (value >> 31);
    return value;
}

// Fold the mixed value into a target-independent 32-bit result.
nuint Integer(ulong value) nothrow
{
    ulong mixed = Mix64(value);
    return (nuint)((uint)mixed ^ (uint)(mixed >> 32));
}

// Ordered hash composition; keep both full-width inputs until the final fold.
nuint Combine(nuint first, nuint second) nothrow
    => Integer(Mix64((ulong)first + 0x9e3779b97f4a7c15ul) ^ (ulong)second);
