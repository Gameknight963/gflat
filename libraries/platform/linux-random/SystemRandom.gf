namespace Linux;

namespace random_detail
{
    private extern int getentropy(void* output, nuint count) nothrow;
}

public static class SystemRandom
{
    /// Fills caller storage using libc getentropy in requests of at most 256 bytes.
    /// Zero count succeeds without a native call or pointer access.
    /// A failure may leave partial output; discard the whole requested result on false.
    public static bool TryFill(byte* output, nuint count) nothrow
    {
        nuint offset = 0;
        while (offset < count)
        {
            nuint chunk = count - offset;
            if (chunk > 256) chunk = 256;
            if (random_detail::getentropy((void*)(output + offset), chunk) != 0) return false;
            offset += chunk;
        }
        return true;
    }
}
