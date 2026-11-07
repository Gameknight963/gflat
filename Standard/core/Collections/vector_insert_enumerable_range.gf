using Standard::Queries;

namespace Standard::Collections;

public void InsertRange<T, S, E>(ref Vector<T> target, nuint index, Enumerable<T, S, E> source)
{
    nuint originalCount = target.Count;
    if (index > originalCount) throw new Exception("Vector insertion index out of range");
    target.AddRange(source);
    // Capture and growth have completed. Relocate only initialized elements.
    Standard::Collections::Rotate<T>(target.AsSpan().Slice(index), originalCount - index);
}

extension Vector<T>
{
    public void InsertRange<S, E>(nuint index, Enumerable<T, S, E> source)
        => Standard::Collections::InsertRange<T, S, E>(ref *this, index, source);
}
