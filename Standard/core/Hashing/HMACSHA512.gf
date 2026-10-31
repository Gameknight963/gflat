namespace Standard::Hashing;

// Incremental HMAC-SHA512 with copied keyed state.
// GetHash reads a snapshot. Reset starts another message with the same key.
public struct HMACSHA512
{
    private SHA512 innerSeed;
    private SHA512 outerSeed;
    private SHA512 running;

    // Initializes from a counted key. Zero count reads no key bytes.
    public HMACSHA512(readonly(char)* key, nuint count) nothrow
    {
        innerSeed = new();
        outerSeed = new();
        char[128] block;
        for (nuint i = 0; i < 128; i++) block[i] = '\0';
        if (count > 128)
        {
            SHA512 hash = new();
            hash.TryAppend(key, count);
            byte[64] digest = hash.GetHash();
            for (nuint i = 0; i < 64; i++) block[i] = (char)digest[i];
        }
        else
            for (nuint i = 0; i < count; i++) block[i] = key[i];
        for (nuint i = 0; i < 128; i++) block[i] = (char)((byte)block[i] ^ (byte)0x36);
        innerSeed.TryAppend(block, 128);
        // Change ipad to opad without retaining another key buffer.
        for (nuint i = 0; i < 128; i++) block[i] = (char)((byte)block[i] ^ (byte)0x6a);
        outerSeed.TryAppend(block, 128);
        running = innerSeed;
    }

    // False rejects the entire append without reading input or changing state.
    public bool TryAppend(readonly(char)* data, nuint count) nothrow
        => running.TryAppend(data, count);

    public void Reset() nothrow { running = innerSeed; }

    public immutable byte[64] GetHash() nothrow
    {
        byte[64] inner = running.GetHash();
        SHA512 outer = outerSeed;
        outer.TryAppend(((byte*)inner).reinterpret<readonly(char)*>(), 64);
        return outer.GetHash();
    }
}
