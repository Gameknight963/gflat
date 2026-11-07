using Standard::Memory;

namespace Standard::Collections;

// Value parameters snapshot aliased arguments before any element changes.
public void Replace<T>(Span<T> values, T oldValue, T newValue,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    for (nuint i = 0; i < values.Length; i++)
        if (equal(&values[i], &oldValue)) values[i] = newValue;
}

// Assignment into initialized storage. Shifted overlaps are rejected.
public void Replace<T>(Span<immutable(T)> source, Span<T> destination, T oldValue, T newValue,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    nuint count = source.Length;
    if (destination.Length < count) throw new Exception("Replacement destination is too short");
    if (count == 0) return;
    immutable(T)* input = source.Data!;
    T* output = destination.Data!;
    nuint from = (input).reinterpret<nuint>();
    nuint to = (output).reinterpret<nuint>();
    nuint stride = (nuint)sizeof(T);
    if (stride != 0 && from != to)
    {
        if ((to > from && (to - from) / stride < count) ||
            (from > to && (from - to) / stride < destination.Length))
            throw new Exception("Replacement spans overlap at different starting locations");
    }
    for (nuint i = 0; i < count; i++)
    {
        if (equal(input + i, &oldValue)) output[i] = newValue;
        else if (from != to) output[i] = input[i];
    }
}

extension Span<T>
{
    public readonly void Replace(T oldValue, T newValue, bool(immutable(T)*, immutable(T)*)* equal)
        => Standard::Collections::Replace<T>(*this, move oldValue, move newValue, equal);
}

extension Span<immutable(T)>
{
    public immutable void Replace(Span<T> destination, T oldValue, T newValue,
        bool(immutable(T)*, immutable(T)*)* equal)
        => Standard::Collections::Replace<T>(*this, destination, move oldValue, move newValue, equal);
}
