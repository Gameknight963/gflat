using Standard::Collections;
using Standard::Memory;

namespace Standard::Numerics;

public void GetItems<T>(ref PseudoRandom random, Span<immutable(T)> choices, Span<T> destination)
{
    if (choices.IsEmpty) throw new Exception("Random choices must not be empty");
    for (nuint i = 0; i < destination.Length; i++)
    {
        nuint selected = (nuint)random.NextULong((ulong)choices.Length);
        T item = choices[selected];
        destination[i] = move item;
    }
}

public Vector<T> GetItems<T>(ref PseudoRandom random, Span<immutable(T)> choices, nuint length)
{
    if (choices.IsEmpty) throw new Exception("Random choices must not be empty");
    Vector<T> result = new();
    result.Reserve(length);
    for (nuint i = 0; i < length; i++)
    {
        nuint selected = (nuint)random.NextULong((ulong)choices.Length);
        result.Add(choices[selected]);
    }
    return move result;
}

extension PseudoRandom
{
    public void GetItems<T>(Span<immutable(T)> choices, Span<T> destination)
        => Standard::Numerics::GetItems<T>(ref *this, choices, destination);

    public Vector<T> GetItems<T>(Span<immutable(T)> choices, nuint length)
        => Standard::Numerics::GetItems<T>(ref *this, choices, length);
}
