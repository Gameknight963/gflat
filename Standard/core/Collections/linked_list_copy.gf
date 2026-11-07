using Standard::Memory;

namespace Standard::Collections;

// Copies in enumeration order into initialized destination elements.
public bool TryCopyTo<T>(immutable(LinkedList<T>)* source, Span<T> destination)
{
    nuint count = source.Count;
    if (destination.Length < count) return false;
    if (count == 0) return true;
    if (source.CopyDestinationOverlaps(destination.Data!, count))
        throw new Exception("Linked list copy destination must not overlap its storage");
    immutable(LinkedList<T>::Node)*? node = source.CopyFirst();
    nuint index = 0;
    while (node != null)
    {
        destination[index] = *LinkedList<T>::CopyValue(node!);
        index++;
        node = LinkedList<T>::CopyNext(node!);
    }
    return true;
}

public void CopyTo<T>(immutable(LinkedList<T>)* source, Span<T> destination)
{
    if (!TryCopyTo<T>(source, destination)) throw new Exception("Copy destination is too short");
}

public void CopyTo<T>(immutable(LinkedList<T>)* source, Span<T> destination, nuint destinationIndex)
    => CopyTo<T>(source, destination.Slice(destinationIndex));

extension LinkedList<T>
{
    public immutable void CopyTo(Span<T> destination)
        => Standard::Collections::CopyTo<T>(this, destination);

    public immutable void CopyTo(Span<T> destination, nuint destinationIndex)
        => Standard::Collections::CopyTo<T>(this, destination, destinationIndex);

    public immutable bool TryCopyTo(Span<T> destination)
        => Standard::Collections::TryCopyTo<T>(this, destination);
}
