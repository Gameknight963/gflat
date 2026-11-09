using Standard::Memory;

namespace Standard::Collections;

// Copies into a new owning buffer. Element copying is required only when called.
public Vector<T> ToVector<T>(Span<immutable(T)> source)
{
    Vector<T> result = new();
    result.Reserve(source.Length);
    for (nuint i = 0; i < source.Length; i++) result.Add(source[i]);
    return move result;
}

extension Span<immutable(T)>
{
    public immutable Vector<T> ToVector()
        => Standard::Collections::ToVector<T>(*this);
}
