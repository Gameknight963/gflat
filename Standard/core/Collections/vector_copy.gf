using Standard::Memory;

namespace Standard::Collections;

// Assigns into initialized destination elements. Copying remains optional for T.
public void CopyTo<T>(immutable(Vector<T>)* source, Span<T> destination)
    => source.AsImmutable().CopyTo(destination);

public void CopyTo<T>(immutable(Vector<T>)* source, Span<T> destination, nuint destinationIndex)
    => source.AsImmutable().CopyTo(destination.Slice(destinationIndex));

public void CopyTo<T>(immutable(Vector<T>)* source, nuint sourceIndex,
    Span<T> destination, nuint destinationIndex, nuint count)
{
    Span<immutable(T)> input = source.AsImmutable().Slice(sourceIndex, count);
    input.CopyTo(destination.Slice(destinationIndex));
}

// False means insufficient space. Element-copy failures still propagate.
public bool TryCopyTo<T>(immutable(Vector<T>)* source, Span<T> destination)
    => source.AsImmutable().TryCopyTo(destination);

extension Vector<T>
{
    public immutable void CopyTo(Span<T> destination)
        => Standard::Collections::CopyTo<T>(this, destination);

    public immutable void CopyTo(Span<T> destination, nuint destinationIndex)
        => Standard::Collections::CopyTo<T>(this, destination, destinationIndex);

    public immutable void CopyTo(nuint sourceIndex, Span<T> destination, nuint destinationIndex, nuint count)
        => Standard::Collections::CopyTo<T>(this, sourceIndex, destination, destinationIndex, count);

    public immutable bool TryCopyTo(Span<T> destination)
        => Standard::Collections::TryCopyTo<T>(this, destination);
}
