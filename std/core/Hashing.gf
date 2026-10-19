namespace std::Hashing;

// Deterministic FNV-1a over counted bytes, including embedded nulls.
// Not suitable for cryptography or keys supplied by hostile users.
nuint Bytes(readonly(char)* data, nuint count)
{
    uint hash = (uint)2166136261;
    for (nuint i = 0; i < count; i++)
        hash = (hash ^ (uint)(byte)data[i]) * (uint)16777619;
    return (nuint)hash;
}

// Fold all 64 input bits into a hash representable on 32- and 64-bit targets.
nuint Integer(ulong value)
{
    uint hash = (uint)value ^ (uint)(value >> 32);
    hash = (hash ^ (hash >> 16)) * (uint)2246822519;
    hash = (hash ^ (hash >> 13)) * (uint)3266489917;
    return (nuint)(hash ^ (hash >> 16));
}
