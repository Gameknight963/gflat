using Standard::Memory;

namespace Standard::Numerics;

public void NextBytes(ref PseudoRandom random, byte* destination, nuint count) nothrow
    => random.Fill(destination.reinterpret<char*>(), count);

public void NextBytes(ref PseudoRandom random, Span<byte> destination) nothrow
{
    if (destination.IsEmpty) return;
    random.Fill((destination.Data!).reinterpret<char*>(), destination.Length);
}

extension PseudoRandom
{
    public void NextBytes(byte* destination, nuint count) nothrow
        => Standard::Numerics::NextBytes(ref *this, destination, count);

    public void NextBytes(Span<byte> destination) nothrow
        => Standard::Numerics::NextBytes(ref *this, destination);
}
