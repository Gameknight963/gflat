namespace std::Hashing;

// RFC 2104 HMAC using SHA-256. Inline state is independently copied.
public struct HMACSHA256
{
    private SHA256 innerSeed;
    private SHA256 outerSeed;
    private SHA256 running;

    public HMACSHA256(readonly(char)* key, nuint count)
    {
        // Reject before touching key; SHA-256 cannot hash a longer key.
        if ((ulong)count > 2305843009213693951ul)
            throw new Exception("HMAC-SHA256 key exceeds the message length limit");
        innerSeed = new();
        outerSeed = new();
        char[64] block;
        for (nuint i = 0; i < 64; i++) block[i] = '\0';
        if (count > 64)
        {
            SHA256 hash = new();
            hash.TryAppend(key, count);
            byte[32] digest = hash.GetHash();
            for (nuint i = 0; i < 32; i++) block[i] = (char)digest[i];
        }
        else
            for (nuint i = 0; i < count; i++) block[i] = key[i];
        for (nuint i = 0; i < 64; i++) block[i] = (char)((byte)block[i] ^ (byte)0x36);
        innerSeed.TryAppend(block, 64);
        // Change ipad to opad without retaining another key buffer.
        for (nuint i = 0; i < 64; i++) block[i] = (char)((byte)block[i] ^ (byte)0x6a);
        outerSeed.TryAppend(block, 64);
        running = innerSeed;
    }

    // False rejects the entire append without reading input or changing state.
    public bool TryAppend(readonly(char)* data, nuint count) nothrow
        => running.TryAppend(data, count);

    public void Reset() nothrow { running = innerSeed; }

    public immutable byte[32] GetHash() nothrow
    {
        byte[32] inner = running.GetHash();
        SHA256 outer = outerSeed;
        outer.TryAppend(((byte*)inner).reinterpret<readonly(char)*>(), 32);
        return outer.GetHash();
    }
}
