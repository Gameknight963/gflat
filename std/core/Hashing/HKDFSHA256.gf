namespace std::Hashing;

/// RFC 5869 key extraction and expansion using the existing HMAC-SHA256 core.
/// Successful calls allocate no heap storage and retain no caller pointers.
public static class HKDFSHA256
{
    private static const ulong MaximumHashBytes = 2305843009213693951ul;
    private static const ulong MaximumMessageBytes = 2305843009213693887ul;

    /// Returns an independent 32-byte PRK; zero salt count represents absent salt.
    /// Excessive salt/input lengths throw before either input is read.
    public static byte[32] Extract(readonly(char)* salt, nuint saltCount,
        readonly(char)* input, nuint inputCount)
    {
        if ((ulong)saltCount > MaximumHashBytes || (ulong)inputCount > MaximumMessageBytes)
            throw new Exception("HKDF-SHA256 extraction input exceeds the message length limit");
        // An empty HMAC key and 32 zero bytes produce the same padded salt key.
        HMACSHA256 hash = new(salt, saltCount);
        hash.TryAppend(input, inputCount);
        return hash.GetHash();
    }

    /// Writes exactly 1..8160 requested bytes, or rejects before input reads/output writes.
    /// Output may overlap PRK, but must not overlap nonempty info storage.
    public static bool TryExpand(readonly(char)* prk, nuint prkCount,
        readonly(char)* info, nuint infoCount, byte* output, nuint outputLength) nothrow
    {
        if (outputLength == 0 || outputLength > 8160 || prkCount < 32
            || (ulong)prkCount > MaximumHashBytes) return false;
        ulong overhead = 1ul;
        if (outputLength > 32) overhead = 33ul;
        if ((ulong)infoCount > MaximumMessageBytes - overhead) return false;
        if (infoCount != 0)
        {
            nuint destination = (output).reinterpret<nuint>();
            nuint source = (info).reinterpret<nuint>();
            if (destination >= source)
            {
                if (destination - source < infoCount) return false;
            }
            else if (source - destination < outputLength) return false;
        }

        // Consume PRK completely before output can overwrite its caller storage.
        HMACSHA256 hash = new(prk, prkCount);
        byte[32] previous;
        nuint position = 0;
        nuint number = 1;
        while (position < outputLength)
        {
            hash.Reset();
            // Preflight accounts for all message pieces, so these appends fit.
            if (number != 1) hash.TryAppend(((byte*)previous).reinterpret<readonly(char)*>(), 32);
            hash.TryAppend(info, infoCount);
            char counter = (char)number;
            hash.TryAppend(&counter, 1);
            previous = hash.GetHash();
            nuint take = outputLength - position;
            if (take > 32) take = 32;
            for (nuint i = 0; i < take; i++) output[position + i] = previous[i];
            position += take;
            number++;
        }
        return true;
    }
}
