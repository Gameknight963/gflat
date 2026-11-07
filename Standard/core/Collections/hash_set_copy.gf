using Standard::Memory;

namespace Standard::Collections;

// Copies in enumeration order into initialized destination elements.
public bool TryCopyTo<T : IHashable>(immutable(HashSet<T>)* source, Span<T> destination)
{
    nuint count = source.Count;
    if (destination.Length < count) return false;
    if (count == 0) return true;
    if (source.CopyDestinationOverlaps(destination.Data!, count))
        throw new Exception("Hash set copy destination must not overlap its storage");
    HashSet<T>::Enumerator iterator = source.GetEnumerator();
    nuint index = 0;
    while (iterator.MoveNext())
    {
        destination[index] = *iterator.Current;
        index++;
    }
    return true;
}

public void CopyTo<T : IHashable>(immutable(HashSet<T>)* source, Span<T> destination)
{
    if (!TryCopyTo<T>(source, destination)) throw new Exception("Copy destination is too short");
}

public void CopyTo<T : IHashable>(immutable(HashSet<T>)* source, Span<T> destination, nuint destinationIndex)
    => CopyTo<T>(source, destination.Slice(destinationIndex));

extension HashSet<T : IHashable>
{
    public immutable void CopyTo(Span<T> destination)
        => Standard::Collections::CopyTo<T>(this, destination);

    public immutable void CopyTo(Span<T> destination, nuint destinationIndex)
        => Standard::Collections::CopyTo<T>(this, destination, destinationIndex);

    public immutable bool TryCopyTo(Span<T> destination)
        => Standard::Collections::TryCopyTo<T>(this, destination);
}
