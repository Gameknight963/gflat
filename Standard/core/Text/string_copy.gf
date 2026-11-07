using Standard::Memory;

namespace Standard::Text;

public void CopyTo(immutable(String)* source, Span<char> destination)
{
    if (destination.Length < source.Length) throw new Exception("String copy destination is too short");
    if (source.Length != 0) global::Memory::Move(destination.Data!, source.Data, source.Length);
}

public void CopyTo(immutable(String)* source, nuint sourceIndex,
    Span<char> destination, nuint destinationIndex, nuint count)
{
    if (sourceIndex > source.Length || count > source.Length - sourceIndex)
        throw new Exception("String copy source range is out of bounds");
    if (destinationIndex > destination.Length || count > destination.Length - destinationIndex)
        throw new Exception("String copy destination range is out of bounds");
    if (count != 0) global::Memory::Move(destination.Data! + destinationIndex, source.Data + sourceIndex, count);
}

public bool TryCopyTo(immutable(String)* source, Span<char> destination) nothrow
{
    if (destination.Length < source.Length) return false;
    if (source.Length != 0) global::Memory::Move(destination.Data!, source.Data, source.Length);
    return true;
}

extension String
{
    public immutable void CopyTo(Span<char> destination)
        => Standard::Text::CopyTo(this, destination);

    public immutable void CopyTo(nuint sourceIndex, Span<char> destination, nuint destinationIndex, nuint count)
        => Standard::Text::CopyTo(this, sourceIndex, destination, destinationIndex, count);

    public immutable bool TryCopyTo(Span<char> destination) nothrow
        => Standard::Text::TryCopyTo(this, destination);
}
