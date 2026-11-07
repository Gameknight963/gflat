namespace gflat.Tests.Std.Numerics;

public sealed partial class BigIntegerWriteBytesTests
{
    private const string Allocator = """
        using Standard;
        using Standard::Memory;
        using Standard::Numerics;

        extern "C" void*? malloc(nuint bytes);
        extern "C" void free(void* storage);
        int requests = 0;
        int allocated = 0;
        int released = 0;
        int failAt = 0;
        bool deny = false;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes)
            {
                requests++;
                if (deny || requests == failAt) return null;
                void*? result = malloc(bytes);
                if (result != null) allocated++;
                return result;
            }
            public replace void Free(void* storage) nothrow { released++; free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NativeMagnitudeCountsAndWritesNeedNoAllocationIncludingZeroAndSignedLimits(int optimization)
        => Run(Allocator + """
            int main()
            {
                {
                    BigInteger[6] values = [new BigInteger(), new BigInteger(-1l),
                        new BigInteger(-9223372036854775807l - 1l),
                        new BigInteger(9223372036854775807l),
                        BigInteger::FromULong(18446744073709551615ul),
                        BigInteger::Parse("-18446744073709551615")];
                    byte[12] output;
                    for (nuint row = 0; row < 6; row++)
                    {
                        immutable(BigInteger)* value = &values[row];
                        BigInteger preserved = *value;
                        int before = requests;
                        deny = true;
                        for (int form = 0; form < 4; form++)
                        {
                            bool unsigned = form >= 2;
                            if (unsigned && value.Sign < 0) continue;
                            nuint required = value.GetByteCount(unsigned);
                            if (required == 0 || required > 9) return 1;
                            for (nuint capacity = 0; capacity <= 10; capacity++)
                            {
                                for (nuint i = 0; i < 12; i++) output[i] = (byte)165;
                                Span<byte> destination = new(&output[1], capacity);
                                if (capacity == 0) destination = new Span<byte>(((nuint)1).reinterpret<byte*>(), 0);
                                nuint written = 77;
                                bool result = value.TryWriteBytes(destination, ref written, unsigned, form % 2 != 0);
                                if (result != (capacity >= required)) return 2;
                                if (result && written != required || !result && written != 0) return 3;
                                if (output[0] != (byte)165 || output[11] != (byte)165) return 4;
                            }
                        }
                        deny = false;
                        if (requests != before || !value.Equals(&preserved)) return 5;
                    }
                }
                if (allocated != released) return 6;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void LargeExportsUseOneScratchAllocationAndLeaveBuffersUnchangedOnFailure(bool negative, int optimization)
        => Run(Allocator + $$"""
            int main()
            {
                {
                    BigInteger value = BigInteger::Parse("{{(negative ? "-" : "")}}999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999");
                    BigInteger preserved = value;
                    byte[128] buffer;
                    for (int form = 0; form < 4; form++)
                    {
                        bool unsigned = form >= 2;
                        if (unsigned && value.Sign < 0) continue;
                        requests = 0;
                        nuint required = value.GetByteCount(unsigned);
                        int countRequests = requests;
                        if (required <= 9 || required >= 126 || countRequests > 1) return 1;
                        requests = 0;
                        nuint written = 77;
                        if (!value.TryWriteBytes(new Span<byte>(&buffer[1], required), ref written, unsigned, form % 2 != 0)
                            || written != required || requests > 1) return 2;
                        int writeRequests = requests;
                        requests = 0;
                        deny = true;
                        written = 77;
                        if (value.TryWriteBytes(new Span<byte>(((nuint)1).reinterpret<byte*>(), 0), ref written, unsigned)
                            || written != 0 || requests != 0) return 3;
                        deny = false;
                        int baseline = allocated - released;
                        for (int failure = 1; failure <= countRequests; failure++)
                        {
                            requests = 0;
                            failAt = failure;
                            nuint previous = 77;
                            bool caught = false;
                            try { previous = value.GetByteCount(unsigned); }
                            catch (BadAllocException* error) { caught = true; }
                            failAt = 0;
                            if (!caught || previous != 77 || allocated - released != baseline || !value.Equals(&preserved)) return 4;
                        }
                        for (int failure = 1; failure <= writeRequests; failure++)
                        {
                            for (nuint i = 0; i < 128; i++) buffer[i] = (byte)165;
                            requests = 0;
                            failAt = failure;
                            written = 77;
                            bool caught = false;
                            try { value.TryWriteBytes(new Span<byte>(&buffer[1], required), ref written, unsigned, form % 2 != 0); }
                            catch (BadAllocException* error) { caught = true; }
                            failAt = 0;
                            if (!caught || written != 0 || allocated - released != baseline || !value.Equals(&preserved)) return 5;
                            for (nuint i = 0; i < 128; i++) if (buffer[i] != (byte)165) return 6;
                        }
                        for (nuint i = 0; i < 128; i++) buffer[i] = (byte)165;
                        written = 77;
                        if (value.TryWriteBytes(new Span<byte>(&buffer[1], required - 1), ref written, unsigned)
                            || written != 0) return 7;
                        for (nuint i = 0; i < 128; i++) if (buffer[i] != (byte)165) return 8;
                        if (!value.Equals(&preserved) || allocated - released != baseline) return 9;
                    }
                }
                if (allocated != released) return 10;
                return 0;
            }
            """, optimization);
}
