namespace Windows;

namespace random_detail
{
    private extern int BCryptGenRandom(void*? algorithm, byte* output, uint count, uint flags) nothrow;
}

public static class SystemRandom
{
    /// Fills caller storage using the Windows system-preferred random generator.
    /// Zero count succeeds without a native call. Counts above uint.MaxValue fail without writing.
    /// A native failure may change output; discard the whole requested result on false.
    public static bool TryFill(byte* output, nuint count) nothrow
    {
        if (count == 0) return true;
        if (sizeof(nint) != 8 || count > (nuint)4294967295ul) return false;
        return random_detail::BCryptGenRandom(null, output, (uint)count, 2u) == 0;
    }
}
