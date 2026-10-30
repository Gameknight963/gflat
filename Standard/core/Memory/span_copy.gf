namespace Standard::Memory;

// Assignment into initialized elements, not construction into raw storage.
// Kept separate so merely using Span<T> never requires T to support copying.
// Receiver snapshots copy only the view header, retaining immutable element protection.
public void CopyTo<T>(Span<immutable(T)> source, Span<T> destination)
=> source.CopyTo(destination);

extension<T> Span<immutable(T)>
{
    public immutable void CopyTo(Span<T> destination)
    {
        Span<immutable(T)> source = *this;
        nuint count = source.Length;
        if (destination.Length < count) throw new Exception("Span copy destination is too short");
        if (count == 0) return;
        immutable(T)* input = source.Data!;
        T* output = destination.Data!;
        if ((input).reinterpret<nuint>() == (output).reinterpret<nuint>()) return;
        // Backward assignment preserves unread source elements when the destination
        // starts inside the source. It is also valid for disjoint later storage.
        if ((output).reinterpret<nuint>() > (input).reinterpret<nuint>())
        {
            while (count != 0) { count--; output[count] = input[count]; }
        }
        else
        {
            for (nuint i = 0; i < count; i++) output[i] = input[i];
        }
    }
}

// False means insufficient destination length; copying errors still propagate.
public bool TryCopyTo<T>(Span<immutable(T)> source, Span<T> destination)
=> source.TryCopyTo(destination);

extension<T> Span<immutable(T)>
{
    public immutable bool TryCopyTo(Span<T> destination)
    {
        Span<immutable(T)> source = *this;
        if (destination.Length < source.Length) return false;
        Standard::Memory::CopyTo<T>(source, destination);
        return true;
    }
}
