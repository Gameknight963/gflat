using Standard::Memory;

namespace Standard::Numerics;

public void Shuffle<T>(ref PseudoRandom random, Span<T> values)
    => Standard::Collections::Shuffle<T>(values, ref random);

extension PseudoRandom
{
    public void Shuffle<T>(Span<T> values)
        => Standard::Collections::Shuffle<T>(values, ref *this);
}
